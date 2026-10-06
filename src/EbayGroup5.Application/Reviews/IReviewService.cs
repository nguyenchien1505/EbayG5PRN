namespace EbayGroup5.Application.Reviews;

public static class ReviewLimits
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 2000;
    public const int MaxLines = 50;
}

public enum ReviewOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public sealed record ReviewResult(ReviewOutcome Outcome, int? ReviewId = null, int? OrderId = null, string? Error = null);
public sealed record ReviewLineEligibility(int OrderItemId, int? ProductId, string Title, bool CanReview, bool Reviewed, int? ReviewId, string? Reason);

public interface IReviewService
{
    /// <summary>Per-line review eligibility for the Buyer who owns the order; null when the order is missing, foreign or the actor is not a Buyer.</summary>
    Task<IReadOnlyList<ReviewLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default);
    Task<ReviewResult> SubmitAsync(int buyerId, int orderItemId, int rating, string? comment, CancellationToken cancellationToken = default);
}
