using System.Data;
using EbayGroup5.Application.Moderation;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Moderation;

public sealed class ModerationService(EbayDbContext db, TimeProvider clock) : IModerationService
{
    private const string Open = "Open";

    // Content row as seen by moderation: includes hidden state; reviews must be order-linked (legacy rows are never public).
    private sealed record Content(int Id, int AuthorId, string Title, string? Rating, string? Comment, string? Reply, string Author,
        DateTime? CreatedAtUtc, DateTime? HiddenAtUtc, DateTime? CommentRemovedAtUtc, string PublicPath);

    private async Task<Content?> Load(ContentKind kind, int id, CancellationToken ct)
    {
        if (kind == ContentKind.Review)
            return await db.Set<Review>().AsNoTracking().Where(x => x.Id == id && x.OrderItemId != null)
                .Select(x => new Content(x.Id, x.ReviewerId!.Value,
                    db.Set<Product>().Where(p => p.Id == x.ProductId).Select(p => p.Title).FirstOrDefault() ?? "Item unavailable",
                    x.Rating + " out of 5", x.Comment, null,
                    db.Set<User>().Where(u => u.Id == x.ReviewerId).Select(u => u.Username).FirstOrDefault() ?? "eBay user",
                    x.CreatedAt, x.HiddenAtUtc, x.CommentRemovedAtUtc, "/Catalog/Reviews/" + x.ProductId)).SingleOrDefaultAsync(ct);
        return await db.Set<SellerFeedback>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new Content(x.Id, x.BuyerId,
                "Feedback for " + (db.Set<User>().Where(u => u.Id == x.SellerId).Select(u => u.Username).FirstOrDefault() ?? "seller"),
                x.Sentiment, x.Comment, x.ReplyText,
                db.Set<User>().Where(u => u.Id == x.BuyerId).Select(u => u.Username).FirstOrDefault() ?? "eBay user",
                x.CreatedAtUtc, x.HiddenAtUtc, x.CommentRemovedAtUtc, "/Sellers/Feedback/" + x.SellerId)).SingleOrDefaultAsync(ct);
    }

    private Task<bool> HasRole(int userId, CancellationToken ct, params string[] roles) =>
        db.Set<User>().AnyAsync(x => x.Id == userId && roles.Contains(x.Role!) && x.Password != null, ct);

    private static string? Clean(string? value, out bool invalid)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        invalid = text != null && (text.Length > ModerationLimits.MaxTextLength || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')));
        return text;
    }

    public async Task<ReportContext?> FindReportableAsync(ContentKind kind, int id, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) return null;
        var content = await Load(kind, id, cancellationToken);
        if (content == null || content.HiddenAtUtc != null) return null;
        return new(kind, id, content.Title, content.Rating, content.CommentRemovedAtUtc == null ? content.Comment : null, content.PublicPath);
    }

    public async Task<ModerationResult> ReportAsync(int reporterId, ContentKind kind, int id, ReportReason reason, string? details, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(reason)) return new(ModerationOutcome.Invalid, "Choose a reason for your report.");
        var text = Clean(details, out var invalid);
        if (invalid) return new(ModerationOutcome.Invalid, $"Details must be at most {ModerationLimits.MaxTextLength} characters without control characters.");
        if (!await HasRole(reporterId, cancellationToken, "Buyer", "Seller")) return new(ModerationOutcome.Forbidden);
        var content = await Load(kind, id, cancellationToken);
        if (content == null || content.HiddenAtUtc != null) return new(ModerationOutcome.NotFound);
        if (content.AuthorId == reporterId) return new(ModerationOutcome.Forbidden, "You can't report your own content.");
        var duplicate = await db.Set<ContentReport>().AnyAsync(x => x.ReporterId == reporterId &&
            (kind == ContentKind.Review ? x.ReviewId == id : x.SellerFeedbackId == id), cancellationToken);
        if (duplicate) return new(ModerationOutcome.Conflict, "You already reported this.", content.PublicPath);
        db.Add(new ContentReport
        {
            ReviewId = kind == ContentKind.Review ? id : null, SellerFeedbackId = kind == ContentKind.SellerFeedback ? id : null,
            ReporterId = reporterId, Reason = reason.ToString(), Details = text, Status = Open, CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new(ModerationOutcome.Conflict, "You already reported this.", content.PublicPath);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
        return new(ModerationOutcome.Success, ReturnPath: content.PublicPath);
    }

    public async Task<IReadOnlyList<QueueItem>?> QueueAsync(int adminId, CancellationToken cancellationToken = default)
    {
        if (!await HasRole(adminId, cancellationToken, "Admin")) return null;
        var open = await db.Set<ContentReport>().AsNoTracking().Where(x => x.Status == Open)
            .GroupBy(x => new { x.ReviewId, x.SellerFeedbackId })
            .Select(g => new { g.Key.ReviewId, g.Key.SellerFeedbackId, Count = g.Count(), Latest = g.Max(x => x.CreatedAtUtc) })
            .OrderByDescending(x => x.Latest).ThenByDescending(x => x.ReviewId ?? x.SellerFeedbackId).Take(ModerationLimits.QueueSize).ToListAsync(cancellationToken);
        var items = new List<QueueItem>();
        foreach (var group in open)
        {
            var kind = group.ReviewId != null ? ContentKind.Review : ContentKind.SellerFeedback;
            var id = group.ReviewId ?? group.SellerFeedbackId!.Value;
            var content = await Load(kind, id, cancellationToken);
            if (content == null) continue;
            var reasons = await db.Set<ContentReport>().AsNoTracking().Where(x => x.Status == Open && (kind == ContentKind.Review ? x.ReviewId == id : x.SellerFeedbackId == id))
                .Select(x => x.Reason).Distinct().ToListAsync(cancellationToken);
            items.Add(new(kind, id, content.Title, content.Comment, group.Count, group.Latest, reasons.Select(Enum.Parse<ReportReason>).Order().ToList(),
                content.HiddenAtUtc != null, content.CommentRemovedAtUtc != null));
        }
        return items;
    }

    public async Task<ModerationDetail?> DetailAsync(int adminId, ContentKind kind, int id, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || !await HasRole(adminId, cancellationToken, "Admin")) return null;
        var content = await Load(kind, id, cancellationToken);
        if (content == null) return null;
        var reports = await db.Set<ContentReport>().AsNoTracking().Where(x => kind == ContentKind.Review ? x.ReviewId == id : x.SellerFeedbackId == id)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, Reporter = db.Set<User>().Where(u => u.Id == x.ReporterId).Select(u => u.Username).FirstOrDefault(), x.Reason, x.Details, x.Status, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var audit = await db.Set<ModerationAudit>().AsNoTracking().Where(x => kind == ContentKind.Review ? x.ReviewId == id : x.SellerFeedbackId == id)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Action, Admin = db.Set<User>().Where(u => u.Id == x.AdminId).Select(u => u.Username).FirstOrDefault(), x.Note, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        return new(kind, id, content.Title, content.Rating, content.Comment, content.Reply, content.Author, content.CreatedAtUtc, content.HiddenAtUtc,
            content.CommentRemovedAtUtc, content.PublicPath,
            reports.Select(x => new ReportView(x.Id, x.Reporter ?? "eBay user", Enum.Parse<ReportReason>(x.Reason), x.Details, x.Status, x.CreatedAtUtc)).ToList(),
            audit.Select(x => new AuditView(Enum.Parse<ModerationAction>(x.Action), x.Admin ?? "Admin", x.Note, x.CreatedAtUtc)).ToList());
    }

    public async Task<ModerationResult> ActAsync(int adminId, ContentKind kind, int id, ModerationAction action, string? note, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(action)) return new(ModerationOutcome.Invalid, "Choose a moderation action.");
        var text = Clean(note, out var invalid);
        if (invalid) return new(ModerationOutcome.Invalid, $"Notes must be at most {ModerationLimits.MaxTextLength} characters without control characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var result = await Act(adminId, kind, id, action, text, cancellationToken);
            if (result.Outcome == ModerationOutcome.Success) await transaction.CommitAsync(cancellationToken);
            else await transaction.RollbackAsync(CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<ModerationResult> Act(int adminId, ContentKind kind, int id, ModerationAction action, string? note, CancellationToken ct)
    {
        var admin = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={adminId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (admin?.Role != "Admin" || admin.Password == null) return new(ModerationOutcome.Forbidden);
        // Lock the content row first so concurrent moderators serialize on the same item.
        var state = kind == ContentKind.Review
            ? await db.Set<Review>().FromSqlInterpolated($"SELECT * FROM dbo.Review WITH (UPDLOCK,HOLDLOCK) WHERE id={id} AND OrderItemId IS NOT NULL")
                .AsNoTracking().Select(x => new { x.Comment, x.HiddenAtUtc, x.CommentRemovedAtUtc }).SingleOrDefaultAsync(ct)
            : await db.Set<SellerFeedback>().FromSqlInterpolated($"SELECT * FROM dbo.SellerFeedback WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}")
                .AsNoTracking().Select(x => new { x.Comment, x.HiddenAtUtc, x.CommentRemovedAtUtc }).SingleOrDefaultAsync(ct);
        if (state == null) return new(ModerationOutcome.NotFound);
        var reports = kind == ContentKind.Review
            ? db.Set<ContentReport>().Where(x => x.ReviewId == id && x.Status == Open)
            : db.Set<ContentReport>().Where(x => x.SellerFeedbackId == id && x.Status == Open);
        var openCount = await reports.CountAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var conflict = action switch
        {
            ModerationAction.Hide when state.HiddenAtUtc != null => "This content is already hidden.",
            ModerationAction.RemoveComment when state.HiddenAtUtc != null => "Hidden content can't have its comment removed. Restore it first.",
            ModerationAction.RemoveComment when state.Comment == null => "This content has no comment to remove.",
            ModerationAction.RemoveComment when state.CommentRemovedAtUtc != null => "This comment was already removed.",
            ModerationAction.Restore when state.HiddenAtUtc == null && state.CommentRemovedAtUtc == null => "This content is already visible.",
            ModerationAction.Dismiss when openCount == 0 => "There are no open reports to dismiss.",
            _ => null
        };
        if (conflict != null) return new(ModerationOutcome.Conflict, conflict);

        DateTime? hidden = action switch { ModerationAction.Hide => now, ModerationAction.Restore => null, _ => state.HiddenAtUtc };
        DateTime? removed = action switch { ModerationAction.RemoveComment => now, ModerationAction.Restore => null, _ => state.CommentRemovedAtUtc };
        if (action != ModerationAction.Dismiss)
        {
            if (kind == ContentKind.Review)
                await db.Set<Review>().Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(r => r.HiddenAtUtc, hidden).SetProperty(r => r.CommentRemovedAtUtc, removed), ct);
            else
                await db.Set<SellerFeedback>().Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(f => f.HiddenAtUtc, hidden).SetProperty(f => f.CommentRemovedAtUtc, removed), ct);
        }
        var resolution = action switch { ModerationAction.Dismiss => "Dismissed", ModerationAction.Restore => null, _ => "Actioned" };
        if (resolution != null && openCount > 0)
            await reports.ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, resolution).SetProperty(r => r.ResolvedAtUtc, now).SetProperty(r => r.ResolvedById, adminId), ct);
        db.Add(new ModerationAudit
        {
            ReviewId = kind == ContentKind.Review ? id : null, SellerFeedbackId = kind == ContentKind.SellerFeedback ? id : null,
            AdminId = adminId, Action = action.ToString(), Note = note, CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        return new(ModerationOutcome.Success);
    }
}
