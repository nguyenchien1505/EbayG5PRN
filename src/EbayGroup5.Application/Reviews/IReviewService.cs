namespace EbayGroup5.Application.Reviews;

public static class ReviewLimits
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 2000;
    public const int MaxLines = 50;
}

public enum ReviewOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public enum ReviewSort { Recent, Highest, Lowest }
public sealed record ReviewResult(ReviewOutcome Outcome, int? ReviewId = null, int? OrderId = null, string? Error = null);
public sealed record ReviewLineEligibility(int OrderItemId, int? ProductId, string Title, bool CanReview, bool Reviewed, int? ReviewId, string? Reason, int? Rating = null);
public sealed record ReviewItemContext(int OrderId, ReviewLineEligibility Line);

/// <summary>Distribution is ordered 5 stars down to 1 star. Average is null when there are no ratings.</summary>
public sealed record RatingSummary(int Ratings, int Reviews, decimal? Average, IReadOnlyList<int> Distribution)
{
    public static RatingSummary Empty { get; } = new(0, 0, null, [0, 0, 0, 0, 0]);
}
/// <summary>ReviewerId is for server-side checks (e.g. hiding Report on own content) and is never rendered.</summary>
public sealed record ProductReview(int Id, string Reviewer, int Rating, string Comment, DateTime CreatedAt, int ReviewerId);
public sealed record ProductReviewPage(RatingSummary Summary, IReadOnlyList<ProductReview> Reviews, int Page, ReviewSort Sort)
{
    public const int PageSize = 10;
    public int TotalPages => (int)Math.Ceiling(Summary.Reviews / (double)PageSize);
}

public interface IReviewService
{
    /// <summary>Per-line review eligibility for the Buyer who owns the order; null when the order is missing, foreign or the actor is not a Buyer.</summary>
    Task<IReadOnlyList<ReviewLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default);
    /// <summary>Eligibility of one order line for its owning Buyer; null when missing, foreign or the actor is not a Buyer.</summary>
    Task<ReviewItemContext?> FindItemAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default);
    Task<ReviewResult> SubmitAsync(int buyerId, int orderItemId, int rating, string? comment, CancellationToken cancellationToken = default);
    /// <summary>Order-linked reviews only. Lists reviews with a comment; the summary counts every rating.</summary>
    Task<ProductReviewPage> ProductReviewsAsync(int productId, int page = 1, ReviewSort sort = ReviewSort.Recent, CancellationToken cancellationToken = default);
}
