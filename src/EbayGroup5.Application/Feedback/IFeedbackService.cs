namespace EbayGroup5.Application.Feedback;

public enum FeedbackSentiment { Positive, Neutral, Negative }

public static class FeedbackLimits
{
    public const int MaxCommentLength = 500;
    public const int MaxReplyLength = 500;
    public static readonly TimeSpan Window = TimeSpan.FromDays(60);
    public static readonly TimeSpan NeutralNegativeWait = TimeSpan.FromDays(7);
}

/// <summary>Clone formula (PLAN §3.2): score = positive − negative; positive % = positive / (positive + negative), neutral excluded.</summary>
public sealed record FeedbackCounts(int Positive, int Neutral, int Negative)
{
    public static FeedbackCounts Empty { get; } = new(0, 0, 0);
    public int Total => Positive + Neutral + Negative;
    public int Score => Positive - Negative;
    public decimal? PositivePercent => Positive + Negative == 0 ? null
        : Math.Round(Positive * 100m / (Positive + Negative), 1, MidpointRounding.AwayFromZero);
}

/// <summary>Q-BIZ-004: score is lifetime (eBay cumulative score); positive % uses the last 12 months.</summary>
public sealed record SellerReputation(FeedbackCounts Lifetime, FeedbackCounts Month1, FeedbackCounts Month6, FeedbackCounts Month12)
{
    public static SellerReputation Empty { get; } = new(FeedbackCounts.Empty, FeedbackCounts.Empty, FeedbackCounts.Empty, FeedbackCounts.Empty);
    public int Total => Lifetime.Total;
    public int Score => Lifetime.Score;
    public decimal? PositivePercent => Month12.PositivePercent;
}

public enum FeedbackRecency { PastMonth, Past6Months, PastYear, OlderThanYear }
public sealed record FeedbackEntry(int Id, FeedbackSentiment Sentiment, string? Comment, string ItemTitle, string Buyer, decimal? UnitPrice,
    DateTime CreatedAtUtc, FeedbackRecency Recency, string? Reply, DateTime? RepliedAtUtc, int BuyerId);
public sealed record SellerFeedbackPage(int SellerId, string Seller, SellerReputation Reputation, IReadOnlyList<FeedbackEntry> Entries,
    int Total, int Page, FeedbackSentiment? Filter)
{
    public const int PageSize = 25;
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
public enum ReplyOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public sealed record ReplyResult(ReplyOutcome Outcome, string? Error = null);

public sealed record FeedbackLineEligibility(int OrderItemId, bool CanLeave, bool PositiveOnly, bool Left, FeedbackSentiment? Sentiment,
    string? Reason, DateTime? NeutralNegativeFromUtc, DateTime? DeadlineUtc);

/// <summary>Combined R1 submission: seller part (Sentiment/SellerComment) and item part (Rating/Comment); at least one part is required.</summary>
public sealed record LeaveFeedbackRequest(int OrderItemId, FeedbackSentiment? Sentiment, string? SellerComment, int? Rating, string? Comment);
public enum LeaveFeedbackOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public sealed record LeaveFeedbackResult(LeaveFeedbackOutcome Outcome, int? OrderId = null, bool FeedbackPosted = false, bool ReviewPosted = false, string? Error = null);

public interface IFeedbackService
{
    /// <summary>Seller-feedback eligibility for one line of the actor's order; null when missing, foreign or the actor is not a Buyer.</summary>
    Task<FeedbackLineEligibility?> FindItemAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default);
    /// <summary>Writes both parts in one transaction; any failure rolls back everything.</summary>
    Task<LeaveFeedbackResult> SubmitAsync(int buyerId, LeaveFeedbackRequest request, CancellationToken cancellationToken = default);
    Task<SellerReputation> ReputationAsync(int sellerId, CancellationToken cancellationToken = default);
    /// <summary>Public F1 profile of a Seller; null when the user is missing or not a Seller.</summary>
    Task<SellerFeedbackPage?> ProfileAsync(int sellerId, FeedbackSentiment? filter = null, int page = 1, CancellationToken cancellationToken = default);
    /// <summary>One permanent reply by the Seller who received the feedback.</summary>
    Task<ReplyResult> ReplyAsync(int sellerId, int feedbackId, string? reply, CancellationToken cancellationToken = default);
}
