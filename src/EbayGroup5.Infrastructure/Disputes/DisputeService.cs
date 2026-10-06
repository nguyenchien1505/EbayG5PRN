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
    private sealed record Existing(int Id, string? Kind, DateTime? ClosedAtUtc);

    public async Task<DisputeEligibility?> EligibilityAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default)
    {
        if (!await IsRole(buyerId, "Buyer", cancellationToken)) return null;
        var order = await db.Set<OrderTable>().AsNoTracking()
            .Where(o => o.BuyerId == buyerId && db.Set<OrderItem>().Any(i => i.Id == orderItemId && i.OrderId == o.Id)).SingleOrDefaultAsync(cancellationToken);
        if (order == null) return null;
        var facts = await Facts(order, orderItemId, cancellationToken);
        return Evaluate(facts, await ExistingFor(orderItemId, cancellationToken), Now);
    }

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
            var order = await db.Set<OrderTable>().FromSqlInterpolated(
                    $"SELECT o.* FROM dbo.OrderTable o WITH (UPDLOCK,HOLDLOCK) WHERE o.id=(SELECT i.orderId FROM dbo.OrderItem i WHERE i.id={request.OrderItemId})")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (order == null || order.BuyerId != buyerId) return new(DisputeResultCode.NotFound);
            var facts = await Facts(order, request.OrderItemId, cancellationToken);
            var existing = await ExistingFor(request.OrderItemId, cancellationToken);
            var eligibility = Evaluate(facts, existing, Now);
            var kind = inr ? eligibility.ItemNotReceived : eligibility.Return;
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
        var rows = await db.Set<Dispute>().FromSqlInterpolated(
                $"SELECT * FROM dbo.Dispute WITH (UPDLOCK,HOLDLOCK) WHERE orderId={orderId} AND OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL AND Kind='ItemNotReceived'")
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            var decision = DisputeRules.Decide(await Snapshot(db, row, ct), DisputeActor.Buyer, new(DisputeAction.ItemReceived), nowUtc, timing);
            if (decision.Code != DisputeResultCode.Success) continue;
            Apply(row, decision.Change!, DisputeActor.Buyer, nowUtc);
            db.Add(Event(row.Id, buyerId, DisputeActor.Buyer, decision.Change!.Event, null, null, null, nowUtc));
        }
    }

    public async Task<DisputeDetail?> FindAsync(int actorId, int disputeId, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<Dispute>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == disputeId && x.OrderItemId != null, cancellationToken);
        if (row == null || await ViewerRole(actorId, row, cancellationToken) is not { } viewer) return null;
        var snapshot = await Snapshot(row, cancellationToken);
        var title = await db.Set<OrderItem>().Where(i => i.Id == row.OrderItemId)
            .Select(i => db.Set<Product>().Where(p => p.Id == i.ProductId).Select(p => p.Title).FirstOrDefault()).FirstOrDefaultAsync(cancellationToken);
        var events = await db.Set<DisputeEvent>().AsNoTracking().Where(x => x.DisputeId == disputeId).OrderBy(x => x.Id).Take(DisputeLimits.MaxEvents)
            .ToListAsync(cancellationToken);
        return new(row.Id, row.OrderId!.Value, row.OrderItemId!.Value, title ?? "Item unavailable", snapshot.Kind, snapshot.Reason,
            row.Preference == null ? null : Enum.Parse<DisputePreference>(row.Preference), snapshot.Status,
            row.Outcome == null ? null : Enum.Parse<DisputeOutcome>(row.Outcome), row.Description, row.CreatedAtUtc!.Value, row.DueAtUtc, row.EscalatedAtUtc, row.ClosedAtUtc,
            snapshot.LineTotal, row.OfferAmount, row.RefundAmount, row.ReturnTracking, viewer, DisputeRules.AvailableActions(snapshot, viewer, Now, timing),
            events.Select(x => new DisputeEventView(x.Id, Enum.Parse<DisputeActor>(x.ActorRole), Enum.Parse<DisputeEventType>(x.Type), x.Message, x.Amount, x.Tracking,
                DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc))).ToList(), timing.IsDemo);
    }

    private Task<bool> IsRole(int userId, string role, CancellationToken ct) => db.Set<User>().AnyAsync(x => x.Id == userId && x.Role == role, ct);

    // Buyer/Seller of the request, else any Admin; null hides the request (404) from everyone else.
    private async Task<DisputeActor?> ViewerRole(int userId, Dispute row, CancellationToken ct)
    {
        var role = await db.Set<User>().Where(x => x.Id == userId).Select(x => x.Role).SingleOrDefaultAsync(ct);
        if (role == "Buyer" && row.RaisedBy == userId) return DisputeActor.Buyer;
        if (role == "Seller" && row.SellerId == userId) return DisputeActor.Seller;
        return role == "Admin" ? DisputeActor.Admin : null;
    }

    private async Task<LineFacts> Facts(OrderTable order, int orderItemId, CancellationToken ct)
    {
        var line = await db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == orderItemId).Select(x => new { x.Quantity, x.UnitPrice }).SingleAsync(ct);
        var payments = order.Status == Delivered ? await db.Set<Payment>().AsNoTracking().Where(x => x.OrderId == order.Id).Take(2).ToListAsync(ct) : [];
        // Same delivered timestamp rule as seller feedback (D03): exactly one Paid payment with paidAt.
        DateTime? deliveredAt = payments is [{ Status: "Paid", PaidAt: { } paidAt }] ? DateTime.SpecifyKind(paidAt, DateTimeKind.Utc) : null;
        decimal? total = line is { Quantity: > 0, UnitPrice: >= 0 } ? line.Quantity!.Value * line.UnitPrice!.Value : null;
        var estimated = order.EstimatedDeliveryUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : (DateTime?)null;
        return new(order.Id, orderItemId, order.SellerId, order.Status, estimated, deliveredAt, total);
    }

    private Task<List<Existing>> ExistingFor(int orderItemId, CancellationToken ct) =>
        db.Set<Dispute>().AsNoTracking().Where(x => x.OrderItemId == orderItemId).Select(x => new Existing(x.Id, x.Kind, x.ClosedAtUtc)).ToListAsync(ct);

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
        static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
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
