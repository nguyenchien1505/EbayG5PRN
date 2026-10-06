using System.Data;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EbayGroup5.Infrastructure.Disputes;

/// <summary>E04 (DISPUTE_AUTOMATION.md). The request state is the outbox: each pass derives what is missing, so reruns and replicas never duplicate.</summary>
public sealed class DisputeAutomationService(EbayDbContext db, TimeProvider clock, DisputeTiming timing, IDisputeService disputes, IOptions<DisputeAutomationOptions> options)
    : IDisputeAutomation
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private int Batch => options.Value.Validated().BatchSize;

    public async Task<DisputeAutomationResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = Now;
        // 1. Overdue System commands (E01 rules re-check each one under the row lock; a lost race is a Conflict without writes).
        var overdue = await db.Set<Dispute>().AsNoTracking()
            .Where(x => x.OrderItemId != null && x.ClosedAtUtc == null && x.DueAtUtc <= now
                && ((x.Status == "Open" && x.SellerRespondedAtUtc == null) || x.Status == "ReturnAccepted"))
            .OrderBy(x => x.DueAtUtc).ThenBy(x => x.Id).Take(Batch).Select(x => new { x.Id, x.Status }).ToListAsync(cancellationToken);
        int escalated = 0, expired = 0;
        foreach (var row in overdue)
        {
            var open = row.Status == "Open";
            var result = await disputes.ActAsSystemAsync(row.Id, open ? DisputeAction.Escalate : DisputeAction.ExpireReturn, cancellationToken);
            if (result.Code != DisputeResultCode.Success) continue;
            if (open) escalated++; else expired++;
        }

        // 2. Open requests still missing the notice of their current state (constants only: NoticeKey and status use different collations).
        var events = db.Set<DisputeEvent>();
        var missing = await db.Set<Dispute>().AsNoTracking()
            .Where(d => d.OrderItemId != null && d.ClosedAtUtc == null && d.DueAtUtc != null)
            .Where(d =>
                (d.Status == "Open" && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.Received)) ||
                (d.Status == "Open" && d.DueAtUtc <= now && d.SellerRespondedAtUtc != null && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.OpenDue)) ||
                (d.Status == "ReturnAccepted" && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.ReturnAccepted)) ||
                (d.Status == "ReturnShipped" && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.ReturnShipped)) ||
                (d.Status == "ReturnShipped" && d.DueAtUtc <= now && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.ReturnShippedDue)) ||
                (d.Status == "Escalated" && !events.Any(e => e.DisputeId == d.Id && e.NoticeKey == DisputeNotices.Escalated)))
            .OrderBy(d => d.Id).Take(Batch).Select(d => d.Id).ToListAsync(cancellationToken);
        var notices = 0;
        foreach (var id in missing) notices += await NotifyAsync(id, cancellationToken);
        return new(escalated, expired, notices);
    }

    public async Task<int> NotifyAsync(int disputeId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Same row lock as the E01 commands: the state cannot move while its notices are written.
            var row = await db.Set<Dispute>().FromSqlInterpolated($"SELECT * FROM dbo.Dispute WITH (UPDLOCK,HOLDLOCK) WHERE id={disputeId} AND OrderItemId IS NOT NULL")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (row == null || row.ClosedAtUtc != null) { await transaction.CommitAsync(cancellationToken); return 0; }
            var now = Now;
            var due = row.DueAtUtc is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : (DateTime?)null;
            var expected = DisputeNotices.Expected(Enum.Parse<DisputeKind>(row.Kind!), Enum.Parse<DisputeStatus>(row.Status!), due, row.SellerRespondedAtUtc != null, now, timing);
            var keys = expected.Select(x => x.Key).ToArray();
            var present = keys.Length == 0 ? [] : await db.Set<DisputeEvent>().AsNoTracking()
                .Where(e => e.DisputeId == disputeId && e.NoticeKey != null && keys.Contains(e.NoticeKey)).Select(e => e.NoticeKey!).ToListAsync(cancellationToken);
            var add = expected.Where(x => !present.Contains(x.Key)).ToList();
            foreach (var notice in add)
                db.Add(new DisputeEvent
                {
                    DisputeId = disputeId, ActorRole = nameof(DisputeActor.System), Type = nameof(DisputeEventType.AutoNotice), Message = notice.Message,
                    NoticeKey = notice.Key, CreatedAtUtc = now
                });
            if (add.Count > 0) await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return add.Count;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another replica wrote the same notice first (UX_DisputeEvent_Notice).
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return 0;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
