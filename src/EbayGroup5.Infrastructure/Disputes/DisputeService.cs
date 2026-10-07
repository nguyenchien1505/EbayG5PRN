using System.Data;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Disputes;

/// <summary>E01 request service (DISPUTES.md). Transition rules live in DisputeRules; this class loads, authorizes and persists.</summary>
public sealed class DisputeService(EbayDbContext db, TimeProvider clock, DisputeTiming timing) : IDisputeService
{
    private const string Delivered = "Delivered";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private sealed record LineFacts(int OrderId, int OrderItemId, int? SellerId, string? Status, DateTime? EstimatedDeliveryUtc, DateTime? DeliveredAtUtc, decimal? LineTotal);
    private sealed record Existing(int Id, int OrderItemId, string? Kind, string? Status, string? Outcome, DateTime? DueAtUtc, DateTime? ClosedAtUtc);

    public async Task<DisputeEligibility?> EligibilityAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default)
    {
        if (!await IsRole(buyerId, "Buyer", cancellationToken)) return null;
        return (await Build(buyerId, [orderItemId], cancellationToken)).GetValueOrDefault(orderItemId);
    }

    public async Task<IReadOnlyDictionary<int, DisputeEligibility>> EligibilityAsync(int buyerId, IReadOnlyCollection<int> orderItemIds, CancellationToken cancellationToken = default)
    {
        var ids = orderItemIds.Distinct().ToArray();
        if (ids.Length > DisputeLimits.MaxBatchLines) throw new ArgumentException("Too many order lines.", nameof(orderItemIds));
        if (ids.Length == 0 || !await IsRole(buyerId, "Buyer", cancellationToken)) return new Dictionary<int, DisputeEligibility>();
        return await Build(buyerId, ids, cancellationToken);
    }

    public async Task<DisputeDraftView?> FindDraftAsync(int buyerId, int orderItemId, DisputeKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || !await IsRole(buyerId, "Buyer", cancellationToken)) return null;
        var draft = await db.Set<DisputeDraft>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.BuyerId == buyerId && x.OrderItemId == orderItemId && x.Kind == kind.ToString(), cancellationToken);
        return draft == null ? null : new(draft.Reason == null ? null : Enum.Parse<DisputeReason>(draft.Reason),
            draft.Preference == null ? null : Enum.Parse<DisputePreference>(draft.Preference), draft.Details, DateTime.SpecifyKind(draft.UpdatedAtUtc, DateTimeKind.Utc));
    }

    public async Task<DraftSaveResult> SaveDraftAsync(int buyerId, DisputeDraftInput input, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(input.Kind) || (input.Reason is { } reason && !DisputeRules.ReasonMatches(input.Kind, reason)))
            return new(DisputeResultCode.Invalid, Error: "Choose a valid reason.");
        if (input.Kind == DisputeKind.ItemNotReceived ? input.Preference is { } p && !Enum.IsDefined(p) : input.Preference != null)
            return new(DisputeResultCode.Invalid, Error: "Choose a valid option.");
        if (!DisputeRules.TryText(input.Details, DisputeLimits.MaxText, required: false, out var details))
            return new(DisputeResultCode.Invalid, Error: $"Details must be at most {DisputeLimits.MaxText} characters without control characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (!await IsRole(buyerId, "Buyer", cancellationToken)) return new(DisputeResultCode.Forbidden);
            // Same order lock as OpenAsync: a late autosave cannot recreate the draft of a request that just opened.
            var order = await LockOrder(input.OrderItemId, cancellationToken);
            if (order == null || order.BuyerId != buyerId) return new(DisputeResultCode.NotFound);
            var eligibility = (await Build(buyerId, [input.OrderItemId], cancellationToken))[input.OrderItemId].For(input.Kind);
            if (!eligibility.CanOpen) return new(DisputeResultCode.Conflict, Error: eligibility.Reason);
            var now = Now;
            var kind = input.Kind.ToString();
            var reasonText = input.Reason?.ToString();
            var preference = input.Preference?.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                MERGE dbo.DisputeDraft WITH (HOLDLOCK) AS t
                USING (SELECT {buyerId} AS BuyerId, {input.OrderItemId} AS OrderItemId, {kind} AS Kind) AS s
                ON t.BuyerId = s.BuyerId AND t.OrderItemId = s.OrderItemId AND t.Kind = s.Kind
                WHEN MATCHED THEN UPDATE SET Reason = {reasonText}, Preference = {preference}, Details = {details}, UpdatedAtUtc = {now}
                WHEN NOT MATCHED THEN INSERT (BuyerId, OrderItemId, Kind, Reason, Preference, Details, UpdatedAtUtc)
                    VALUES ({buyerId}, {input.OrderItemId}, {kind}, {reasonText}, {preference}, {details}, {now});
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(DisputeResultCode.Success, now);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<OrderTable?> LockOrder(int orderItemId, CancellationToken ct) => db.Set<OrderTable>().FromSqlInterpolated(
            $"SELECT o.* FROM dbo.OrderTable o WITH (UPDLOCK,HOLDLOCK) WHERE o.id=(SELECT i.orderId FROM dbo.OrderItem i WHERE i.id={orderItemId})")
        .AsNoTracking().SingleOrDefaultAsync(ct);

    public async Task<DisputeResult> OpenAsync(int buyerId, OpenDisputeRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Kind) || !DisputeRules.ReasonMatches(request.Kind, request.Reason))
            return new(DisputeResultCode.Invalid, Error: "Choose a reason for this request.");
        var inr = request.Kind == DisputeKind.ItemNotReceived;
        if (inr ? request.Preference is not { } p || !Enum.IsDefined(p) : request.Preference != null)
            return new(DisputeResultCode.Invalid, Error: "Choose whether you'd like a refund or still want the item.");
        if (!DisputeRules.TryText(request.Details, DisputeLimits.MaxText, required: !inr, out var details))
            return new(DisputeResultCode.Invalid, Error: !inr && string.IsNullOrWhiteSpace(request.Details)
                ? "Add details about the problem." : $"Details must be at most {DisputeLimits.MaxText} characters without control characters.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (!await IsRole(buyerId, "Buyer", cancellationToken)) return new(DisputeResultCode.Forbidden);
            // The order lock serializes concurrent opens for any line of the order; unique indexes are the final guard.
            var order = await LockOrder(request.OrderItemId, cancellationToken);
            if (order == null || order.BuyerId != buyerId) return new(DisputeResultCode.NotFound);
            var kind = (await Build(buyerId, [request.OrderItemId], cancellationToken))[request.OrderItemId].For(request.Kind);
            if (!kind.CanOpen) return new(DisputeResultCode.Conflict, Error: kind.Reason, ExistingDisputeId: kind.ExistingDisputeId);
            var now = Now;
            var row = new Dispute
            {
                OrderId = order.Id, OrderItemId = request.OrderItemId, RaisedBy = buyerId, SellerId = order.SellerId, Kind = request.Kind.ToString(),
                Reason = request.Reason.ToString(), Preference = request.Preference?.ToString(), Description = details, Status = nameof(DisputeStatus.Open),
                CreatedAtUtc = now, DueAtUtc = timing.AddBusinessDays(now, DisputeTiming.SellerResponseBusinessDays)
            };
            db.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            db.Add(Event(row.Id, buyerId, DisputeActor.Buyer, DisputeEventType.Opened, null, null, null, now));
            await db.SaveChangesAsync(cancellationToken);
            // G5-N03: the draft goes only once the request exists (same transaction).
            await db.Set<DisputeDraft>().Where(x => x.BuyerId == buyerId && x.OrderItemId == request.OrderItemId && x.Kind == row.Kind)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(DisputeResultCode.Success, row.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var active = await db.Set<Dispute>().AsNoTracking().Where(x => x.OrderItemId == request.OrderItemId && x.ClosedAtUtc == null).Select(x => (int?)x.Id).FirstOrDefaultAsync(CancellationToken.None);
            return new(DisputeResultCode.Conflict, Error: "This item already has a request.", ExistingDisputeId: active);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<DisputeResult> ActAsync(int actorId, int disputeId, DisputeCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Action == DisputeAction.ItemReceived) return new(DisputeResultCode.Invalid, Error: "Confirm the item arrived from the order details page.");
        return await Act(disputeId, command, actorId, cancellationToken);
    }

    public Task<DisputeResult> ActAsSystemAsync(int disputeId, DisputeAction action, CancellationToken cancellationToken = default) =>
        Act(disputeId, new DisputeCommand(action), null, cancellationToken);

    private async Task<DisputeResult> Act(int disputeId, DisputeCommand command, int? actorId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var row = await db.Set<Dispute>().FromSqlInterpolated($"SELECT * FROM dbo.Dispute WITH (UPDLOCK,HOLDLOCK) WHERE id={disputeId} AND OrderItemId IS NOT NULL")
                .SingleOrDefaultAsync(ct);
            if (row == null) return new(DisputeResultCode.NotFound);
            DisputeActor actor;
            if (actorId == null) actor = DisputeActor.System;
            else if (await ViewerRole(actorId.Value, row, ct) is { } role) actor = role;
            else return new(DisputeResultCode.NotFound);
            var now = Now;
            var decision = DisputeRules.Decide(await Snapshot(row, ct), actor, command, now, timing);
            if (decision.Code != DisputeResultCode.Success) return new(decision.Code, disputeId, decision.Error);
            Apply(row, decision.Change!, actor, now);
            if (command.Action == DisputeAction.Decide) row.Resolution = decision.Change!.Message;
            db.Add(Event(row.Id, actorId, actor, decision.Change!.Event, decision.Change.Message, decision.Change.Amount, decision.Change.Tracking, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return new(DisputeResultCode.Success, disputeId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>B06 Confirm received: closes the order's open item-not-received requests inside the caller's transaction (caller saves).</summary>
    internal static async Task ItemReceivedAsync(EbayDbContext db, int buyerId, int orderId, DateTime nowUtc, DisputeTiming timing, CancellationToken ct)
    {
        // Q-PERF-001: the caller holds the order row's UPDLOCK (as does OpenAsync), so no new request can appear for this order; seek the
        // order's rows (DbUp 0014) and lock just them instead of range-locking dbo.Dispute.
        var rows = await db.Set<Dispute>().FromSqlInterpolated(
                $"SELECT * FROM dbo.Dispute WITH (UPDLOCK, FORCESEEK(IX_Dispute_Order(orderId))) WHERE orderId={orderId} AND OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL AND Kind='ItemNotReceived'")
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            var decision = DisputeRules.Decide(await Snapshot(db, row, ct), DisputeActor.Buyer, new(DisputeAction.ItemReceived), nowUtc, timing);
            if (decision.Code != DisputeResultCode.Success) continue;
            Apply(row, decision.Change!, DisputeActor.Buyer, nowUtc);
            db.Add(Event(row.Id, buyerId, DisputeActor.Buyer, decision.Change!.Event, null, null, null, nowUtc));
        }
    }

    public Task<DisputeDetail?> FindAsync(int actorId, int disputeId, CancellationToken cancellationToken = default) =>
        FindAsync(actorId, disputeId, 1, cancellationToken);

    public async Task<DisputeDetail?> FindAsync(int actorId, int disputeId, int timelinePage, CancellationToken cancellationToken = default)
    {
        if (timelinePage < 1 || timelinePage > int.MaxValue / DisputeLimits.TimelinePageSize) throw new ArgumentOutOfRangeException(nameof(timelinePage));
        var row = await db.Set<Dispute>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == disputeId && x.OrderItemId != null, cancellationToken);
        if (row == null || await ViewerRole(actorId, row, cancellationToken) is not { } viewer) return null;
        var snapshot = await Snapshot(row, cancellationToken);
        var title = await db.Set<OrderItem>().Where(i => i.Id == row.OrderItemId)
            .Select(i => db.Set<Product>().Where(p => p.Id == i.ProductId).Select(p => p.Title).FirstOrDefault()).FirstOrDefaultAsync(cancellationToken);
        var names = await db.Set<User>().AsNoTracking().Where(x => x.Id == row.RaisedBy || x.Id == row.SellerId).Select(x => new { x.Id, x.Username }).ToListAsync(cancellationToken);
        var buyerName = names.FirstOrDefault(x => x.Id == row.RaisedBy)?.Username;
        var sellerName = names.FirstOrDefault(x => x.Id == row.SellerId)?.Username;
        var allEvents = db.Set<DisputeEvent>().AsNoTracking().Where(x => x.DisputeId == disputeId);
        var total = await allEvents.CountAsync(cancellationToken);
        // Newest first: the latest activity is what a participant acts on; older pages follow.
        var events = await allEvents.OrderByDescending(x => x.Id).Skip((timelinePage - 1) * DisputeLimits.TimelinePageSize).Take(DisputeLimits.TimelinePageSize)
            .ToListAsync(cancellationToken);
        // E05: only Admin viewers see who acted (admin usernames never reach buyers or sellers).
        var actorIds = viewer == DisputeActor.Admin ? events.Where(x => x.ActorId != null).Select(x => x.ActorId!.Value).Distinct().ToArray() : [];
        var actors = actorIds.Length == 0 ? new Dictionary<int, string?>()
            : await db.Set<User>().AsNoTracking().Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username, cancellationToken);
        return new DisputeDetail(row.Id, row.OrderId!.Value, row.OrderItemId!.Value, title ?? "Item unavailable", snapshot.Kind, snapshot.Reason,
            row.Preference == null ? null : Enum.Parse<DisputePreference>(row.Preference), snapshot.Status,
            row.Outcome == null ? null : Enum.Parse<DisputeOutcome>(row.Outcome), row.Description, Utc(row.CreatedAtUtc)!.Value, Utc(row.DueAtUtc), Utc(row.EscalatedAtUtc),
            Utc(row.ClosedAtUtc), snapshot.LineTotal, row.OfferAmount, row.RefundAmount, row.ReturnTracking, viewer, DisputeRules.AvailableActions(snapshot, viewer, Now, timing),
            events.Select(x => new DisputeEventView(x.Id, Enum.Parse<DisputeActor>(x.ActorRole), Enum.Parse<DisputeEventType>(x.Type), x.Message, x.Amount, x.Tracking,
                DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc)) { ActorName = x.ActorId is { } a ? actors.GetValueOrDefault(a) : null }).ToList(), timing.IsDemo)
        {
            BuyerName = buyerName, SellerName = sellerName,
            Counterpart = viewer == DisputeActor.Buyer ? sellerName : viewer == DisputeActor.Seller ? buyerName : null,
            EventPage = timelinePage, EventTotal = total
        };
    }

    public async Task<DisputeList?> ListAsync(int actorId, DisputeListQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page < 1 || query.Page > int.MaxValue / DisputeLimits.ListPageSize || query.Kind is { } k && !Enum.IsDefined(k) || query.State is { } st && !Enum.IsDefined(st))
            throw new ArgumentOutOfRangeException(nameof(query));
        var viewer = await ListRole(actorId, cancellationToken);
        if (viewer == null) return null;
        var rows = Participating(actorId, viewer.Value);
        if (query.Kind is { } kind) { var name = kind.ToString(); rows = rows.Where(x => x.Kind == name); }
        if (query.State == DisputeListState.Open) rows = rows.Where(x => x.ClosedAtUtc == null);
        else if (query.State == DisputeListState.Closed) rows = rows.Where(x => x.ClosedAtUtc != null);
        var total = await rows.CountAsync(cancellationToken);
        var buyerView = viewer == DisputeActor.Buyer;
        var page = await (from d in rows
                          join i in db.Set<OrderItem>() on d.OrderItemId equals i.Id
                          orderby d.Id descending
                          select new
                          {
                              d.Id, OrderId = d.OrderId!.Value, OrderItemId = d.OrderItemId!.Value, d.Kind, d.Reason, d.Status, d.Outcome, d.CreatedAtUtc, d.DueAtUtc, d.OfferAmount,
                              i.Quantity, i.UnitPrice,
                              Title = db.Set<Product>().Where(p => p.Id == i.ProductId).Select(p => p.Title).FirstOrDefault(),
                              Counterpart = db.Set<User>().Where(u => u.Id == (buyerView ? d.SellerId : d.RaisedBy)).Select(u => u.Username).FirstOrDefault()
                          }).Skip((query.Page - 1) * DisputeLimits.ListPageSize).Take(DisputeLimits.ListPageSize).ToListAsync(cancellationToken);
        return new(viewer.Value, query, page.Select(x => new DisputeListItem(x.Id, x.OrderId, x.OrderItemId, x.Title ?? "Item unavailable", Enum.Parse<DisputeKind>(x.Kind!),
            Enum.Parse<DisputeReason>(x.Reason!), Enum.Parse<DisputeStatus>(x.Status!), x.Outcome == null ? null : Enum.Parse<DisputeOutcome>(x.Outcome), Utc(x.CreatedAtUtc)!.Value,
            Utc(x.DueAtUtc), (x.Quantity ?? 0) * (x.UnitPrice ?? 0), x.OfferAmount, x.Counterpart)).ToList(), total);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<DisputeSummary>>> RequestsForOrderAsync(int actorId, int orderId, CancellationToken cancellationToken = default)
    {
        if (await ListRole(actorId, cancellationToken) is not { } viewer) return new Dictionary<int, IReadOnlyList<DisputeSummary>>();
        var found = await Participating(actorId, viewer).Where(x => x.OrderId == orderId).OrderByDescending(x => x.Id)
            .Select(x => new Existing(x.Id, x.OrderItemId!.Value, x.Kind, x.Status, x.Outcome, x.DueAtUtc, x.ClosedAtUtc)).ToListAsync(cancellationToken);
        return found.GroupBy(x => x.OrderItemId).ToDictionary(g => g.Key, g => (IReadOnlyList<DisputeSummary>)g.Select(Summary).ToList());
    }

    // E03 lists: the Buyer sees requests they raised, the Seller the requests they received; any other role sees none.
    private async Task<DisputeActor?> ListRole(int actorId, CancellationToken ct) =>
        await db.Set<User>().Where(x => x.Id == actorId).Select(x => x.Role).SingleOrDefaultAsync(ct) switch
        {
            "Buyer" => DisputeActor.Buyer,
            "Seller" => DisputeActor.Seller,
            _ => null
        };

    private IQueryable<Dispute> Participating(int actorId, DisputeActor viewer)
    {
        var rows = db.Set<Dispute>().AsNoTracking().Where(x => x.OrderItemId != null);
        return viewer == DisputeActor.Buyer ? rows.Where(x => x.RaisedBy == actorId) : rows.Where(x => x.SellerId == actorId);
    }

    private static DisputeSummary Summary(Existing d) => new(d.Id, Enum.Parse<DisputeKind>(d.Kind!), Enum.Parse<DisputeStatus>(d.Status!),
        d.Outcome == null ? null : Enum.Parse<DisputeOutcome>(d.Outcome), Utc(d.DueAtUtc));

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private Task<bool> IsRole(int userId, string role, CancellationToken ct) => db.Set<User>().AnyAsync(x => x.Id == userId && x.Role == role, ct);

    // Buyer/Seller of the request, else an Admin with a credential (D05/E05); null hides the request (404) from everyone else.
    private async Task<DisputeActor?> ViewerRole(int userId, Dispute row, CancellationToken ct)
    {
        var user = await db.Set<User>().Where(x => x.Id == userId).Select(x => new { x.Role, Credential = x.Password != null }).SingleOrDefaultAsync(ct);
        if (user?.Role == "Buyer" && row.RaisedBy == userId) return DisputeActor.Buyer;
        if (user?.Role == "Seller" && row.SellerId == userId) return DisputeActor.Seller;
        return user is { Role: "Admin", Credential: true } ? DisputeActor.Admin : null;
    }

    private Task<bool> IsAdmin(int userId, CancellationToken ct) => db.Set<User>().AnyAsync(x => x.Id == userId && x.Role == "Admin" && x.Password != null, ct);

    public async Task<DisputeQueue?> QueueAsync(int adminId, DisputeQueueQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page < 1 || query.Page > int.MaxValue / DisputeLimits.QueuePageSize || !Enum.IsDefined(query.State)) throw new ArgumentOutOfRangeException(nameof(query));
        if (!await IsAdmin(adminId, cancellationToken)) return null;
        var rows = db.Set<Dispute>().AsNoTracking().Where(x => x.OrderItemId != null && x.EscalatedAtUtc != null);
        rows = query.State == DisputeQueueState.Waiting ? rows.Where(x => x.Status == "Escalated" && x.ClosedAtUtc == null) : rows.Where(x => x.ClosedAtUtc != null);
        var total = await rows.CountAsync(cancellationToken);
        var ordered = query.State == DisputeQueueState.Waiting ? rows.OrderBy(x => x.DueAtUtc).ThenBy(x => x.Id) : rows.OrderByDescending(x => x.ClosedAtUtc).ThenByDescending(x => x.Id);
        var page = await (from d in ordered.Skip((query.Page - 1) * DisputeLimits.QueuePageSize).Take(DisputeLimits.QueuePageSize)
                          join i in db.Set<OrderItem>() on d.OrderItemId equals i.Id
                          select new
                          {
                              d.Id, OrderId = d.OrderId!.Value, d.Kind, d.Reason, d.Status, d.Outcome, d.EscalatedAtUtc, d.DueAtUtc, d.ClosedAtUtc, d.RefundAmount, i.Quantity, i.UnitPrice,
                              Title = db.Set<Product>().Where(p => p.Id == i.ProductId).Select(p => p.Title).FirstOrDefault(),
                              Buyer = db.Set<User>().Where(u => u.Id == d.RaisedBy).Select(u => u.Username).FirstOrDefault(),
                              Seller = db.Set<User>().Where(u => u.Id == d.SellerId).Select(u => u.Username).FirstOrDefault()
                          }).ToListAsync(cancellationToken);
        // The join can reorder rows; restore the queue order in memory.
        var items = page.Select(x => new DisputeQueueItem(x.Id, x.OrderId, x.Title ?? "Item unavailable", Enum.Parse<DisputeKind>(x.Kind!), Enum.Parse<DisputeReason>(x.Reason!),
            Enum.Parse<DisputeStatus>(x.Status!), x.Outcome == null ? null : Enum.Parse<DisputeOutcome>(x.Outcome), Utc(x.EscalatedAtUtc), Utc(x.DueAtUtc), Utc(x.ClosedAtUtc),
            (x.Quantity ?? 0) * (x.UnitPrice ?? 0), x.RefundAmount, x.Buyer, x.Seller));
        items = query.State == DisputeQueueState.Waiting ? items.OrderBy(x => x.DueAtUtc).ThenBy(x => x.Id) : items.OrderByDescending(x => x.ClosedAtUtc).ThenByDescending(x => x.Id);
        return new(query, items.ToList(), total);
    }

    // One query each for lines, payments of delivered orders and existing requests; same rules for one line or a Purchases page.
    private async Task<Dictionary<int, DisputeEligibility>> Build(int buyerId, int[] ids, CancellationToken ct)
    {
        var lines = await (from item in db.Set<OrderItem>().AsNoTracking()
                           join order in db.Set<OrderTable>().AsNoTracking() on item.OrderId equals order.Id
                           where ids.Contains(item.Id) && order.BuyerId == buyerId
                           select new
                           {
                               item.Id, item.Quantity, item.UnitPrice, OrderId = order.Id, order.SellerId, order.Status, order.EstimatedDeliveryUtc,
                               Title = db.Set<Product>().Where(p => p.Id == item.ProductId).Select(p => p.Title).FirstOrDefault()
                           }).ToListAsync(ct);
        var delivered = lines.Where(x => x.Status == Delivered).Select(x => x.OrderId).Distinct().ToArray();
        var payments = delivered.Length == 0 ? [] : await db.Set<Payment>().AsNoTracking().Where(x => x.OrderId != null && delivered.Contains(x.OrderId.Value))
            .Select(x => new { OrderId = x.OrderId!.Value, x.Status, x.PaidAt }).ToListAsync(ct);
        var found = lines.Select(x => x.Id).ToArray();
        var existing = await db.Set<Dispute>().AsNoTracking().Where(x => x.OrderItemId != null && found.Contains(x.OrderItemId.Value)).OrderByDescending(x => x.Id)
            .Select(x => new Existing(x.Id, x.OrderItemId!.Value, x.Kind, x.Status, x.Outcome, x.DueAtUtc, x.ClosedAtUtc)).ToListAsync(ct);
        var now = Now;
        return lines.ToDictionary(x => x.Id, x =>
        {
            // Same delivered timestamp rule as seller feedback (D03): exactly one Paid payment with paidAt.
            var paid = payments.Where(p => p.OrderId == x.OrderId).ToList();
            DateTime? deliveredAt = paid.Count == 1 && paid[0].Status == "Paid" && paid[0].PaidAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
            decimal? total = x.Quantity is > 0 && x.UnitPrice is >= 0 ? x.Quantity.Value * x.UnitPrice.Value : null;
            var estimated = x.EstimatedDeliveryUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : (DateTime?)null;
            var mine = existing.Where(d => d.OrderItemId == x.Id).ToList();
            return Evaluate(new(x.OrderId, x.Id, x.SellerId, x.Status, estimated, deliveredAt, total), mine, now) with
            {
                ItemTitle = x.Title ?? "Item unavailable",
                LineTotal = total,
                Requests = mine.Select(Summary).ToList()
            };
        });
    }

    private static DisputeEligibility Evaluate(LineFacts f, IReadOnlyList<Existing> existing, DateTime now)
    {
        var active = existing.FirstOrDefault(x => x.ClosedAtUtc == null);
        KindEligibility Common(DisputeKind kind, DateTime? opens, DateTime? closes, string? reason)
        {
            var same = existing.FirstOrDefault(x => x.Kind == kind.ToString());
            reason ??= active != null ? "This item already has an open request." :
                same != null ? "You already opened this request for this item. Closed requests can't be reopened." :
                f.SellerId == null ? "Seller details are unavailable for this order." :
                f.LineTotal is not > 0 ? "This order line has incomplete price details." : null;
            return new(reason == null, reason, opens, closes, active?.Id ?? same?.Id);
        }
        string? inrReason = f.Status switch
        {
            Delivered => "You confirmed this item arrived. If there's a problem with it, start a return instead.",
            "Pending" or "Shipped" => f.EstimatedDeliveryUtc is not { } est ? "The estimated delivery date for this order is unavailable." :
                now <= est ? "You can report this item after its estimated delivery date." :
                now > est + DisputeTiming.OpenWindow ? "The 30-day window to report this item has ended." : null,
            _ => "This order isn't eligible for an item not received request."
        };
        string? returnReason = f.Status != Delivered ? "You can return this item after it's delivered." :
            f.DeliveredAtUtc is not { } delivered ? "The delivery date for this order is unavailable." :
            now > delivered + DisputeTiming.OpenWindow ? "The 30-day return window for this item has ended." : null;
        return new(f.OrderId, f.OrderItemId, f.EstimatedDeliveryUtc,
            Common(DisputeKind.ItemNotReceived, f.EstimatedDeliveryUtc, f.EstimatedDeliveryUtc + DisputeTiming.OpenWindow, inrReason),
            Common(DisputeKind.Return, f.DeliveredAtUtc, f.DeliveredAtUtc + DisputeTiming.OpenWindow, returnReason));
    }

    private Task<DisputeSnapshot> Snapshot(Dispute row, CancellationToken ct) => Snapshot(db, row, ct);

    private static async Task<DisputeSnapshot> Snapshot(EbayDbContext db, Dispute row, CancellationToken ct)
    {
        var line = await db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == row.OrderItemId).Select(x => new { x.Quantity, x.UnitPrice }).SingleAsync(ct);
        return new(Enum.Parse<DisputeKind>(row.Kind!), Enum.Parse<DisputeReason>(row.Reason!), Enum.Parse<DisputeStatus>(row.Status!), Utc(row.CreatedAtUtc)!.Value,
            Utc(row.DueAtUtc), row.SellerRespondedAtUtc != null, (line.Quantity ?? 0) * (line.UnitPrice ?? 0), row.OfferAmount);
    }

    private static void Apply(Dispute row, DisputeChange change, DisputeActor actor, DateTime now)
    {
        row.Status = change.Status.ToString();
        row.DueAtUtc = change.DueAtUtc;
        row.OfferAmount = change.OfferAmount;
        if (change.Outcome is { } outcome) { row.Outcome = outcome.ToString(); row.ClosedAtUtc = now; row.RefundAmount = change.RefundAmount; }
        if (change.ReturnTracking != null) row.ReturnTracking = change.ReturnTracking;
        if (change.Escalates) row.EscalatedAtUtc = now;
        if (actor == DisputeActor.Seller) row.SellerRespondedAtUtc ??= now;
    }

    private static DisputeEvent Event(int disputeId, int? actorId, DisputeActor role, DisputeEventType type, string? message, decimal? amount, string? tracking, DateTime now) =>
        new() { DisputeId = disputeId, ActorId = actorId, ActorRole = role.ToString(), Type = type.ToString(), Message = message, Amount = amount, Tracking = tracking, CreatedAtUtc = now };
}
