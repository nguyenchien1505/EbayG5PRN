namespace EbayGroup5.Application.Moderation;

public enum ContentKind { Review, SellerFeedback }
public enum ReportReason { Offensive, PersonalInfo, Spam, Other }
public enum ModerationAction { Hide, RemoveComment, Restore, Dismiss }
public enum ModerationOutcome { Success, Invalid, NotFound, Forbidden, Conflict }

public static class ModerationLimits
{
    public const int MaxTextLength = 500;
    public const int QueueSize = 50;
}

/// <summary>Public summary of reportable content; ReturnPath is the public page to go back to.</summary>
public sealed record ReportContext(ContentKind Kind, int Id, string Title, string? Rating, string? Comment, string ReturnPath);
public sealed record ModerationResult(ModerationOutcome Outcome, string? Error = null, string? ReturnPath = null);

public sealed record QueueItem(ContentKind Kind, int Id, string Title, string? Excerpt, int OpenReports, DateTime LatestReportUtc,
    IReadOnlyList<ReportReason> Reasons, bool Hidden, bool CommentRemoved);
public sealed record ReportView(int Id, string Reporter, ReportReason Reason, string? Details, string Status, DateTime CreatedAtUtc);
public sealed record AuditView(ModerationAction Action, string Admin, string? Note, DateTime CreatedAtUtc);
public sealed record ModerationDetail(ContentKind Kind, int Id, string Title, string? Rating, string? Comment, string? Reply, string Author,
    DateTime? CreatedAtUtc, DateTime? HiddenAtUtc, DateTime? CommentRemovedAtUtc, string PublicPath,
    IReadOnlyList<ReportView> Reports, IReadOnlyList<AuditView> Audit);

public interface IModerationService
{
    /// <summary>Visible content the actor may report; null when missing or hidden.</summary>
    Task<ReportContext?> FindReportableAsync(ContentKind kind, int id, CancellationToken cancellationToken = default);
    Task<ModerationResult> ReportAsync(int reporterId, ContentKind kind, int id, ReportReason reason, string? details, CancellationToken cancellationToken = default);
    /// <summary>Admin-only; null when the actor is not an Admin in the database.</summary>
    Task<IReadOnlyList<QueueItem>?> QueueAsync(int adminId, CancellationToken cancellationToken = default);
    Task<ModerationDetail?> DetailAsync(int adminId, ContentKind kind, int id, CancellationToken cancellationToken = default);
    /// <summary>Changes moderation state, resolves reports and writes exactly one audit row in one transaction.</summary>
    Task<ModerationResult> ActAsync(int adminId, ContentKind kind, int id, ModerationAction action, string? note, CancellationToken cancellationToken = default);
}
