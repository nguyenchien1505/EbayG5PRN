namespace EbayGroup5.Application.Feedback;

public enum FeedbackSentiment { Positive, Neutral, Negative }

public static class FeedbackLimits
{
    public const int MaxCommentLength = 500;
    public static readonly TimeSpan Window = TimeSpan.FromDays(60);
    public static readonly TimeSpan NeutralNegativeWait = TimeSpan.FromDays(7);
}

/// <summary>Clone formula (PLAN §3.2): score = positive − negative; positive % = positive / (positive + negative), neutral excluded.</summary>
public sealed record SellerReputation(int Positive, int Neutral, int Negative)
{
    public static SellerReputation Empty { get; } = new(0, 0, 0);
    public int Total => Positive + Neutral + Negative;
    public int Score => Positive - Negative;
    public decimal? PositivePercent => Positive + Negative == 0 ? null
        : Math.Round(Positive * 100m / (Positive + Negative), 1, MidpointRounding.AwayFromZero);
}

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
}
