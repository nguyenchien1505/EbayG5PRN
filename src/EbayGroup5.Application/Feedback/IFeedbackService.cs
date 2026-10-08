namespace EbayGroup5.Application.Feedback;

public enum FeedbackSentiment { Positive, Neutral, Negative }

public static class FeedbackLimits
{
    public const int MaxCommentLength = 500;
    public const int MaxReplyLength = 500;
    public static readonly TimeSpan Window = TimeSpan.FromDays(60);
    public static readonly TimeSpan NeutralNegativeWait = TimeSpan.FromDays(7);
    public const int MaxBatchIds = 100;
    public const int MinDetailRating = 1;
    public const int MaxDetailRating = 5;
}

/// <summary>Bound from the "Feedback" configuration section (FEEDBACK.md, Quyết định H01).</summary>
public sealed class FeedbackDisplayOptions
{
    /// <summary>eBay shows detailed seller ratings once a member receives at least 10; demos may lower it.</summary>
    public int DetailedRatingsMinimum { get; set; } = 10;
}

/// <summary>Score = positive − negative; positive % = positive / (positive + negative), neutral excluded. Applied to buyer-week counts (H01).</summary>
public sealed record FeedbackCounts(int Positive, int Neutral, int Negative)
{
    public static FeedbackCounts Empty { get; } = new(0, 0, 0);
    public int Total => Positive + Neutral + Negative;
    public int Score => Positive - Negative;
    public decimal? PositivePercent => Positive + Negative == 0 ? null
        : Math.Round(Positive * 100m / (Positive + Negative), 1, MidpointRounding.AwayFromZero);
}

/// <summary>One detailed seller rating line: average of 1–5 stars over the last 12 months (null when unrated) and its rating count.</summary>
public sealed record DetailRating(decimal? Average, int Count)
{
    public static DetailRating None { get; } = new(null, 0);
}

/// <summary>eBay DSR block. ShippingCost is automatic 5 stars per feedback (clone orders ship free); visibility counts buyer-rated values only.</summary>
public sealed record DetailedSellerRatings(DetailRating Description, DetailRating ShippingCost, DetailRating ShippingSpeed, DetailRating Communication)
{
    public static DetailedSellerRatings Empty { get; } = new(DetailRating.None, DetailRating.None, DetailRating.None, DetailRating.None);
    public int BuyerRated => Description.Count + ShippingSpeed.Count + Communication.Count;
}

public enum FeedbackStarColor { Yellow, Blue, Turquoise, Purple, Red, Green, Silver }
public sealed record FeedbackStar(FeedbackStarColor Color, bool Shooting)
{
    private static readonly (int Min, FeedbackStar Star)[] Tiers =
    [
        (1_000_000, new(FeedbackStarColor.Silver, true)), (500_000, new(FeedbackStarColor.Green, true)), (100_000, new(FeedbackStarColor.Red, true)),
        (50_000, new(FeedbackStarColor.Purple, true)), (25_000, new(FeedbackStarColor.Turquoise, true)), (10_000, new(FeedbackStarColor.Yellow, true)),
        (5_000, new(FeedbackStarColor.Green, false)), (1_000, new(FeedbackStarColor.Red, false)), (500, new(FeedbackStarColor.Purple, false)),
        (100, new(FeedbackStarColor.Turquoise, false)), (50, new(FeedbackStarColor.Blue, false)), (10, new(FeedbackStarColor.Yellow, false)),
    ];

    /// <summary>eBay feedback star for a score; null below 10 points.</summary>
    public static FeedbackStar? For(int score) => Tiers.FirstOrDefault(x => score >= x.Min).Star;
}

/// <summary>
/// Lifetime/Month* count every visible feedback (eBay "Feedback ratings" table). Unique/Unique12 count each buyer at most once per
/// sentiment per transaction week (Monday–Sunday, Pacific time), which drives the score (lifetime) and positive % (12 months).
/// </summary>
public sealed record SellerReputation(FeedbackCounts Lifetime, FeedbackCounts Month1, FeedbackCounts Month6, FeedbackCounts Month12,
    FeedbackCounts Unique, FeedbackCounts Unique12, DetailedSellerRatings Details)
{
    public static SellerReputation Empty { get; } = new(FeedbackCounts.Empty, FeedbackCounts.Empty, FeedbackCounts.Empty, FeedbackCounts.Empty,
        FeedbackCounts.Empty, FeedbackCounts.Empty, DetailedSellerRatings.Empty);
    public int Total => Lifetime.Total;
    public int Score => Unique.Score;
    public decimal? PositivePercent => Unique12.PositivePercent;
    public FeedbackStar? Star => FeedbackStar.For(Score);

    /// <summary>eBay-style compact feedback score for search cards: 999, 1.2K, 12K, 1.5M (truncated, never rounded up).</summary>
    public static string CompactScore(int score)
    {
        static string Trim(long tenths, string unit) => (tenths % 10 == 0 ? (tenths / 10).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : (tenths / 10m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) + unit;
        long value = score, magnitude = Math.Abs(value);
        var text = magnitude < 1_000 ? magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : magnitude < 1_000_000 ? Trim(magnitude / 100, "K") : Trim(magnitude / 100_000, "M");
        return value < 0 ? "-" + text : text;
    }
}

public enum FeedbackRecency { PastMonth, Past6Months, PastYear, OlderThanYear }
/// <summary>BuyerScore is the buyer's feedback score as a buyer; the clone has no feedback for buyers yet, so it is 0 (H02).</summary>
public sealed record FeedbackEntry(int Id, FeedbackSentiment Sentiment, string? Comment, string ItemTitle, string Buyer, decimal? UnitPrice,
    DateTime CreatedAtUtc, FeedbackRecency Recency, string? Reply, DateTime? RepliedAtUtc, int BuyerId, int? ProductId = null, int BuyerScore = 0);

/// <summary>eBay classic profile tabs (F2). The clone only stores feedback received as a seller.</summary>
public enum FeedbackTab { All, Buyer, Seller, Left }
public enum FeedbackPeriod { All, PastMonth, Past6Months, PastYear }
/// <summary>Relevant: entries with a visible comment first, then newest (clone approximation of eBay "Most relevant").</summary>
public enum FeedbackSort { Relevant, Recent }
public sealed record FeedbackProfileQuery(FeedbackTab Tab = FeedbackTab.All, FeedbackSentiment? Sentiment = null, FeedbackPeriod Period = FeedbackPeriod.All,
    string? Search = null, FeedbackSort Sort = FeedbackSort.Relevant, int Page = 1, int PageSize = SellerFeedbackPage.PageSize)
{
    public static readonly int[] PageSizes = [25, 50, 100];
    public const int MaxSearchLength = 100;
    public const int MaxPageSize = 100;
}

public sealed record SellerCategory(int Id, string Name, int Listings);
/// <summary>About-this-seller facts from clone data only (ADR-003 §3); missing facts stay null/empty and are not shown.</summary>
public sealed record SellerSummary(int SellerId, string Name, string? StoreName, DateTime? JoinedAtUtc, long ItemsSold, bool RespondsWithin24Hours,
    IReadOnlyList<SellerCategory> Categories, int? ContactProductId);

public sealed record SellerFeedbackPage(int SellerId, string Seller, SellerReputation Reputation, IReadOnlyList<FeedbackEntry> Entries,
    int Total, int Page, FeedbackSentiment? Filter)
{
    public const int PageSize = 25;
    public FeedbackProfileQuery Query { get; init; } = new();
    public SellerSummary? Summary { get; init; }
    public int TotalPages => (int)Math.Ceiling(Total / (double)Query.PageSize);
}
public enum ReplyOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public sealed record ReplyResult(ReplyOutcome Outcome, string? Error = null);

public sealed record FeedbackLineEligibility(int OrderItemId, bool CanLeave, bool PositiveOnly, bool Left, FeedbackSentiment? Sentiment,
    string? Reason, DateTime? NeutralNegativeFromUtc, DateTime? DeadlineUtc);

/// <summary>R1 "Rate more details about your purchase" and "Did this item arrive on time?"; only sent with the seller part.</summary>
public sealed record FeedbackDetails(bool? ArrivedOnTime = null, int? Description = null, int? ShippingTime = null, int? Communication = null)
{
    public bool IsEmpty => ArrivedOnTime == null && Description == null && ShippingTime == null && Communication == null;
}

/// <summary>
/// Combined R1 submission: seller part (Sentiment + Details) and item part (Rating); at least one part is required.
/// The single comment goes to every part being posted.
/// </summary>
public sealed record LeaveFeedbackRequest(int OrderItemId, FeedbackSentiment? Sentiment, int? Rating, string? Comment, FeedbackDetails? Details = null);
public enum LeaveFeedbackOutcome { Success, Invalid, NotFound, Forbidden, Conflict }
public sealed record LeaveFeedbackResult(LeaveFeedbackOutcome Outcome, int? OrderId = null, bool FeedbackPosted = false, bool ReviewPosted = false, string? Error = null);

public interface IFeedbackService
{
    /// <summary>Buyer-rated detailed seller ratings needed before the DSR block is shown (Feedback:DetailedRatingsMinimum).</summary>
    int DetailedRatingsMinimum { get; }
    /// <summary>Seller-feedback eligibility for one line of the actor's order; null when missing, foreign or the actor is not a Buyer.</summary>
    Task<FeedbackLineEligibility?> FindItemAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default);
    /// <summary>Writes both parts in one transaction; any failure rolls back everything.</summary>
    Task<LeaveFeedbackResult> SubmitAsync(int buyerId, LeaveFeedbackRequest request, CancellationToken cancellationToken = default);
    Task<SellerReputation> ReputationAsync(int sellerId, CancellationToken cancellationToken = default);
    /// <summary>D06 catalog cards: same rules as ReputationAsync; every requested id is present (Empty when no feedback). At most MaxBatchIds distinct ids.</summary>
    Task<IReadOnlyDictionary<int, SellerReputation>> ReputationsAsync(IReadOnlyCollection<int> sellerIds, CancellationToken cancellationToken = default);
    /// <summary>Public F2 profile of a Seller; null when the user is missing or not a Seller. Invalid query values throw ArgumentException.</summary>
    Task<SellerFeedbackPage?> ProfileAsync(int sellerId, FeedbackProfileQuery query, CancellationToken cancellationToken = default);
    Task<SellerFeedbackPage?> ProfileAsync(int sellerId, FeedbackSentiment? filter = null, int page = 1, CancellationToken cancellationToken = default);
    /// <summary>About this seller (item page, profile header); null when the user is missing or not a Seller.</summary>
    Task<SellerSummary?> SummaryAsync(int sellerId, CancellationToken cancellationToken = default);
    /// <summary>One permanent reply by the Seller who received the feedback.</summary>
    Task<ReplyResult> ReplyAsync(int sellerId, int feedbackId, string? reply, CancellationToken cancellationToken = default);
}
