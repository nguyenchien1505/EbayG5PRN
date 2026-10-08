using System.Data;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Infrastructure.Reviews;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Reputation;

public sealed class FeedbackService(EbayDbContext db, TimeProvider clock, FeedbackDisplayOptions? display = null) : IFeedbackService
{
    private readonly int detailedMinimum = (display ?? new()).DetailedRatingsMinimum is var min and >= 1 and <= 1_000 ? min
        : throw new ArgumentOutOfRangeException(nameof(display), "Feedback:DetailedRatingsMinimum must be 1–1000.");
    private const string Delivered = "Delivered";
    private const string WaitMessage = "You need to wait 7 days before you can leave neutral or negative feedback for this seller.";
    private const string AlreadyLeft = "You already left feedback for this seller.";
    private const string DetailsNeedSentiment = "Choose Positive, Neutral or Negative for the seller before rating more details.";
    private sealed record WindowRow(int Seller, string Sentiment, int All, int M1, int M6, int M12, int UniqueAll, int Unique12);
    private sealed record DetailRow(int Seller, decimal? Description, int DescriptionCount, decimal? ShippingTime, int ShippingTimeCount,
        decimal? Communication, int CommunicationCount, int Feedback12);

    // H01 (FEEDBACK.md): one buyer moves the score at most once per sentiment per transaction week, Monday–Sunday Pacific time.
    // TR_SellerFeedback_TransactionWeek (0015) stores the week at insert with AT TIME ZONE, so reads only count indexed columns.
    private const string VisibleWithWeek = """
        SELECT f.SellerId, f.Sentiment, f.CreatedAtUtc, f.DescriptionRating, f.ShippingTimeRating, f.CommunicationRating,
               CAST(f.BuyerId AS bigint) * 10000 + DATEDIFF(day, '19000101', f.TransactionWeek) / 7 AS BuyerWeek
        FROM dbo.SellerFeedback f
        WHERE f.HiddenAtUtc IS NULL AND f.SellerId IN (SELECT CAST([value] AS int) FROM OPENJSON(@ids))
        """;

    private const string WindowSql = "WITH f AS (" + VisibleWithWeek + """
        )
        SELECT SellerId AS Seller, Sentiment, COUNT(*) AS [All],
               SUM(CASE WHEN CreatedAtUtc > @m1 THEN 1 ELSE 0 END) AS M1, SUM(CASE WHEN CreatedAtUtc > @m6 THEN 1 ELSE 0 END) AS M6,
               SUM(CASE WHEN CreatedAtUtc > @m12 THEN 1 ELSE 0 END) AS M12,
               COUNT(DISTINCT BuyerWeek) AS UniqueAll,
               COUNT(DISTINCT CASE WHEN CreatedAtUtc > @m12 THEN BuyerWeek END) AS Unique12
        FROM f GROUP BY SellerId, Sentiment
        """;

    // Detailed seller ratings over the last 12 months of visible feedback.
    private const string DetailSql = "WITH f AS (" + VisibleWithWeek + """
        )
        SELECT SellerId AS Seller,
               AVG(CAST(DescriptionRating AS decimal(9,4))) AS Description, COUNT(DescriptionRating) AS DescriptionCount,
               AVG(CAST(ShippingTimeRating AS decimal(9,4))) AS ShippingTime, COUNT(ShippingTimeRating) AS ShippingTimeCount,
               AVG(CAST(CommunicationRating AS decimal(9,4))) AS Communication, COUNT(CommunicationRating) AS CommunicationCount,
               COUNT(*) AS Feedback12
        FROM f WHERE CreatedAtUtc > @m12 GROUP BY SellerId
        """;

    // Single visibility rule for reputation and the public profile: not hidden by moderation (D05).
    private IQueryable<SellerFeedback> Visible() => db.Set<SellerFeedback>().AsNoTracking().Where(x => x.HiddenAtUtc == null);

    public async Task<SellerReputation> ReputationAsync(int sellerId, CancellationToken cancellationToken = default) =>
        (await ReputationsAsync([sellerId], cancellationToken))[sellerId];

    public async Task<IReadOnlyDictionary<int, SellerReputation>> ReputationsAsync(IReadOnlyCollection<int> sellerIds, CancellationToken cancellationToken = default)
    {
        var ids = sellerIds.Distinct().ToArray();
        if (ids.Length > FeedbackLimits.MaxBatchIds) throw new ArgumentException("Too many sellers.", nameof(sellerIds));
        if (ids.Length == 0) return new Dictionary<int, SellerReputation>();
        var now = clock.GetUtcNow().UtcDateTime;
        DateTime m1 = now.AddMonths(-1), m6 = now.AddMonths(-6), m12 = now.AddMonths(-12);
        var json = System.Text.Json.JsonSerializer.Serialize(ids);
        SqlParameter[] Parameters() =>
        [
            new("@ids", SqlDbType.NVarChar, -1) { Value = json }, new("@m1", SqlDbType.DateTime2) { Value = m1 },
            new("@m6", SqlDbType.DateTime2) { Value = m6 }, new("@m12", SqlDbType.DateTime2) { Value = m12 }
        ];
        var rows = await db.Database.SqlQueryRaw<WindowRow>(WindowSql, Parameters()).ToListAsync(cancellationToken);
        var details = (await db.Database.SqlQueryRaw<DetailRow>(DetailSql, Parameters()).ToListAsync(cancellationToken)).ToDictionary(x => x.Seller);
        SellerReputation Reputation(int seller)
        {
            FeedbackCounts Counts(Func<WindowRow, int> pick)
            {
                int Of(FeedbackSentiment s) => rows.Where(x => x.Seller == seller && x.Sentiment == s.ToString()).Sum(pick);
                return new(Of(FeedbackSentiment.Positive), Of(FeedbackSentiment.Neutral), Of(FeedbackSentiment.Negative));
            }
            return new(Counts(x => x.All), Counts(x => x.M1), Counts(x => x.M6), Counts(x => x.M12), Counts(x => x.UniqueAll), Counts(x => x.Unique12),
                details.TryGetValue(seller, out var d) ? Details(d) : DetailedSellerRatings.Empty);
        }
        return ids.ToDictionary(x => x, Reputation);
    }

    private static DetailRating Rating(decimal? average, int count) =>
        count == 0 ? DetailRating.None : new(Math.Round(average!.Value, 1, MidpointRounding.AwayFromZero), count);

    // Reasonable shipping cost: clone orders ship free, so eBay's automatic 5 stars applies to every feedback in the window.
    private static DetailedSellerRatings Details(DetailRow d) => new(Rating(d.Description, d.DescriptionCount),
        Rating(d.Feedback12 == 0 ? null : 5m, d.Feedback12), Rating(d.ShippingTime, d.ShippingTimeCount), Rating(d.Communication, d.CommunicationCount));

    public int DetailedRatingsMinimum => detailedMinimum;

    public Task<SellerFeedbackPage?> ProfileAsync(int sellerId, FeedbackSentiment? filter = null, int page = 1, CancellationToken cancellationToken = default) =>
        ProfileAsync(sellerId, new FeedbackProfileQuery(Sentiment: filter, Page: page), cancellationToken);

    public async Task<SellerFeedbackPage?> ProfileAsync(int sellerId, FeedbackProfileQuery query, CancellationToken cancellationToken = default)
    {
        var search = query.Search?.Trim();
        if (string.IsNullOrEmpty(search)) search = null;
        if (query.Page < 1 || query.PageSize is < 1 or > FeedbackProfileQuery.MaxPageSize || query.Page > int.MaxValue / query.PageSize
            || !Enum.IsDefined(query.Tab) || !Enum.IsDefined(query.Period) || !Enum.IsDefined(query.Sort)
            || (query.Sentiment is { } f && !Enum.IsDefined(f)) || search?.Length > FeedbackProfileQuery.MaxSearchLength)
            throw new ArgumentException("Invalid feedback filters.");
        var summary = await SummaryAsync(sellerId, cancellationToken);
        if (summary == null) return null;
        var reputation = await ReputationAsync(sellerId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var rowsQuery = Visible().Where(x => x.SellerId == sellerId);
        // Feedback for buyers and feedback this member left do not exist in the clone: those tabs are honestly empty.
        if (query.Tab is FeedbackTab.Buyer or FeedbackTab.Left) rowsQuery = rowsQuery.Where(x => false);
        if (query.Sentiment != null) rowsQuery = rowsQuery.Where(x => x.Sentiment == query.Sentiment.Value.ToString());
        if (query.Period != FeedbackPeriod.All)
        {
            var since = now.AddMonths(query.Period switch { FeedbackPeriod.PastMonth => -1, FeedbackPeriod.Past6Months => -6, _ => -12 });
            rowsQuery = rowsQuery.Where(x => x.CreatedAtUtc > since);
        }
        if (search != null)
        {
            // eBay: "Search Feedback received as seller with an item title or ID".
            int? itemId = int.TryParse(search.TrimStart('#'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;
            rowsQuery = rowsQuery.Where(x => db.Set<OrderItem>().Any(i => i.Id == x.OrderItemId &&
                (i.ProductId == itemId || db.Set<Product>().Any(p => p.Id == i.ProductId && p.Title != null && p.Title.Contains(search)))));
        }
        var total = await rowsQuery.CountAsync(cancellationToken);
        var ordered = query.Sort == FeedbackSort.Recent ? rowsQuery.OrderByDescending(x => x.Id)
            : rowsQuery.OrderByDescending(x => x.CommentRemovedAtUtc == null && x.Comment != null).ThenByDescending(x => x.Id);
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new
            {
                x.Id, x.Sentiment, x.CreatedAtUtc, x.BuyerId,
                // A removed comment also removes the seller reply attached to it (eBay: no reply to removed content).
                Comment = x.CommentRemovedAtUtc == null ? x.Comment : null,
                ReplyText = x.CommentRemovedAtUtc == null ? x.ReplyText : null,
                RepliedAtUtc = x.CommentRemovedAtUtc == null ? x.RepliedAtUtc : null,
                Buyer = db.Set<User>().Where(u => u.Id == x.BuyerId).Select(u => u.Username).FirstOrDefault(),
                Item = db.Set<OrderItem>().Where(i => i.Id == x.OrderItemId)
                    .Select(i => new { i.UnitPrice, i.ProductId, Title = db.Set<Product>().Where(p => p.Id == i.ProductId).Select(p => p.Title).FirstOrDefault() }).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        var entries = rows.Select(x => new FeedbackEntry(x.Id, Enum.Parse<FeedbackSentiment>(x.Sentiment), x.Comment, x.Item?.Title ?? "Item unavailable",
            Mask(x.Buyer), x.Item?.UnitPrice, x.CreatedAtUtc, Recency(x.CreatedAtUtc, now), x.ReplyText, x.RepliedAtUtc, x.BuyerId,
            x.Item?.Title == null ? null : x.Item.ProductId)).ToList();
        return new(sellerId, summary.Name, reputation, entries, total, query.Page, query.Sentiment) { Query = query with { Search = search }, Summary = summary };
    }

    public async Task<SellerSummary?> SummaryAsync(int sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await db.Set<User>().AsNoTracking().Where(x => x.Id == sellerId && x.Role == "Seller")
            .Select(x => new { x.Username, x.JoinedAtUtc }).SingleOrDefaultAsync(cancellationToken);
        if (seller == null) return null;
        var store = await db.Set<Store>().AsNoTracking().Where(x => x.SellerId == sellerId && x.StoreName != null && x.StoreName.Trim() != "")
            .OrderBy(x => x.Id).Select(x => x.StoreName).FirstOrDefaultAsync(cancellationToken);
        var sold = await db.Set<OrderItem>().AsNoTracking()
            .Where(i => db.Set<OrderTable>().Any(o => o.Id == i.OrderId && o.SellerId == sellerId) && i.Quantity > 0)
            .SumAsync(i => (long?)i.Quantity, cancellationToken) ?? 0;
        // Same listing rule as the catalog: fixed price, titled, priced.
        var listings = db.Set<Product>().AsNoTracking().Where(p => p.SellerId == sellerId && p.IsAuction == false && p.Title != null && p.Title.Trim() != "" && p.Price != null && p.Price >= 0);
        var categories = await listings.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Name = db.Set<Category>().Where(c => c.Id == g.Key).Select(c => c.Name).FirstOrDefault() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name).Take(7).ToListAsync(cancellationToken);
        var contact = await listings.OrderByDescending(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return new(sellerId, string.IsNullOrWhiteSpace(seller.Username) ? "eBay seller" : seller.Username, store,
            seller.JoinedAtUtc is { } joined ? DateTime.SpecifyKind(joined, DateTimeKind.Utc) : null, sold,
            await RespondsWithin24HoursAsync(sellerId, cancellationToken),
            categories.Select(x => new SellerCategory(x.Id, x.Name ?? "Other", x.Count)).ToList(), contact);
    }

    private sealed record TurnRow(long? Seconds);

    // H02: a buyer "turn" is the first buyer message after a seller message (or the conversation start) in the last 90 days.
    // Median time to the next seller message ≤ 24 h → "Usually responds within 24 hours". Unanswered turns older than 24 h count as slow.
    private const string TurnsSql = """
        WITH m AS (
            SELECT ConversationId, Sequence, SentAtUtc, senderId, ConversationBuyerId AS BuyerId,
                   LAG(senderId) OVER (PARTITION BY ConversationId ORDER BY Sequence) AS PreviousSender
            FROM dbo.Message WITH (INDEX(IX_Message_SellerTurns))
            WHERE ConversationSellerId = @seller AND ConversationId IS NOT NULL AND SentAtUtc > @since
        ), turns AS (
            SELECT TOP (200) ConversationId, Sequence, SentAtUtc FROM m
            WHERE senderId = BuyerId AND (PreviousSender IS NULL OR PreviousSender <> BuyerId)
            ORDER BY SentAtUtc DESC
        )
        SELECT CASE WHEN r.SentAtUtc IS NOT NULL THEN DATEDIFF_BIG(second, t.SentAtUtc, r.SentAtUtc)
                    WHEN t.SentAtUtc < @dayAgo THEN CAST(9223372036854775807 AS bigint) END AS Seconds
        FROM turns t
        OUTER APPLY (SELECT TOP (1) x.SentAtUtc FROM dbo.Message x
                     WHERE x.ConversationId = t.ConversationId AND x.Sequence > t.Sequence AND x.senderId = @seller ORDER BY x.Sequence) r
        """;

    private async Task<bool> RespondsWithin24HoursAsync(int sellerId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Database.SqlQueryRaw<TurnRow>(TurnsSql,
            new SqlParameter("@seller", SqlDbType.Int) { Value = sellerId }, new SqlParameter("@since", SqlDbType.DateTime2) { Value = now.AddDays(-90) },
            new SqlParameter("@dayAgo", SqlDbType.DateTime2) { Value = now.AddDays(-1) }).ToListAsync(ct);
        var seconds = rows.Where(x => x.Seconds != null).Select(x => x.Seconds!.Value).Order().ToArray();
        if (seconds.Length == 0) return false;
        var median = seconds.Length % 2 == 1 ? seconds[seconds.Length / 2]
            : (seconds[seconds.Length / 2 - 1] / 2) + (seconds[seconds.Length / 2] / 2);
        return median <= 86_400;
    }

    // eBay-style partial identity: first and last character only; never exposes email or IDs.
    internal static string Mask(string? username)
    {
        var visible = (username ?? "").Where(char.IsLetterOrDigit).ToArray();
        if (visible.Length == 0) return "Buyer";
        return visible.Length <= 2 ? visible[0] + "***" : $"{visible[0]}***{visible[^1]}";
    }

    internal static FeedbackRecency Recency(DateTime created, DateTime now) =>
        created > now.AddMonths(-1) ? FeedbackRecency.PastMonth : created > now.AddMonths(-6) ? FeedbackRecency.Past6Months :
        created > now.AddMonths(-12) ? FeedbackRecency.PastYear : FeedbackRecency.OlderThanYear;

    public async Task<ReplyResult> ReplyAsync(int sellerId, int feedbackId, string? reply, CancellationToken cancellationToken = default)
    {
        var text = reply?.Trim();
        if (string.IsNullOrEmpty(text)) return new(ReplyOutcome.Invalid, "Enter a reply.");
        if (text.Length > FeedbackLimits.MaxReplyLength || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
            return new(ReplyOutcome.Invalid, $"Replies must be at most {FeedbackLimits.MaxReplyLength} characters without control characters.");
        if (!await db.Set<User>().AnyAsync(x => x.Id == sellerId && x.Role == "Seller", cancellationToken)) return new(ReplyOutcome.Forbidden);
        var now = clock.GetUtcNow().UtcDateTime;
        // Single conditional UPDATE: ownership and "only once" are checked atomically by SQL.
        var updated = await db.Set<SellerFeedback>().Where(x => x.Id == feedbackId && x.SellerId == sellerId && x.ReplyText == null && x.HiddenAtUtc == null && x.CommentRemovedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.ReplyText, text).SetProperty(f => f.RepliedAtUtc, now), cancellationToken);
        if (updated == 1) return new(ReplyOutcome.Success);
        var state = await Visible().Where(x => x.Id == feedbackId && x.SellerId == sellerId).Select(x => new { x.CommentRemovedAtUtc }).SingleOrDefaultAsync(cancellationToken);
        return state == null ? new(ReplyOutcome.NotFound)
            : state.CommentRemovedAtUtc != null ? new(ReplyOutcome.Conflict, "Replies are closed because this feedback comment was removed.")
            : new(ReplyOutcome.Conflict, "You already replied to this feedback.");
    }

    public async Task<FeedbackLineEligibility?> FindItemAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default)
    {
        var orderId = await db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == orderItemId).Select(x => x.OrderId).SingleOrDefaultAsync(cancellationToken);
        if (orderId == null) return null;
        var lines = await ListForOrderAsync(buyerId, orderId.Value, cancellationToken, orderItemId);
        return lines?.SingleOrDefault();
    }

    public Task<IReadOnlyList<FeedbackLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default) =>
        ListForOrderAsync(buyerId, orderId, cancellationToken, null);

    private async Task<IReadOnlyList<FeedbackLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken ct, int? onlyItem)
    {
        if (!await db.Set<User>().AnyAsync(x => x.Id == buyerId && x.Role == "Buyer", ct)) return null;
        var order = await db.Set<OrderTable>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, ct);
        if (order == null) return null;
        var deliveredAt = order.Status == Delivered ? DeliveredAt(await db.Set<Payment>().AsNoTracking().Where(x => x.OrderId == orderId).Take(2).ToListAsync(ct)) : null;
        var items = db.Set<OrderItem>().AsNoTracking().Where(x => x.OrderId == orderId);
        if (onlyItem != null) items = items.Where(x => x.Id == onlyItem);
        var lines = await items.OrderBy(x => x.Id).Take(ReviewLimits.MaxLines)
            .Select(x => new { x.Id, Sentiment = db.Set<SellerFeedback>().Where(f => f.OrderItemId == x.Id).Select(f => f.Sentiment).FirstOrDefault() })
            .ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return lines.Select(x => Eligibility(x.Id, order, deliveredAt, x.Sentiment, now)).ToList();
    }

    private static DateTime? DeliveredAt(IReadOnlyList<Payment> payments) =>
        payments is [{ Status: "Paid", PaidAt: { } paidAt }] ? DateTime.SpecifyKind(paidAt, DateTimeKind.Utc) : null;

    private static FeedbackLineEligibility Eligibility(int orderItemId, OrderTable order, DateTime? deliveredAt, string? existing, DateTime now)
    {
        if (existing != null)
            return new(orderItemId, false, false, true, Enum.Parse<FeedbackSentiment>(existing), AlreadyLeft, null, null);
        var reason = order.Status != Delivered ? "You can leave feedback after the order is delivered." :
            order.SellerId == null ? "Seller details are unavailable for this order." :
            deliveredAt == null ? "The delivery date for this order is unavailable." :
            now > deliveredAt.Value + FeedbackLimits.Window ? "The 60-day feedback period for this order has ended." : null;
        if (reason != null) return new(orderItemId, false, false, false, null, reason, null, null);
        var opens = deliveredAt!.Value + FeedbackLimits.NeutralNegativeWait;
        return new(orderItemId, true, now < opens, false, null, null, opens, deliveredAt.Value + FeedbackLimits.Window);
    }

    public async Task<LeaveFeedbackResult> SubmitAsync(int buyerId, LeaveFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        // R1: one comment box ("Tell us more about this item", 500) that goes to every part being posted.
        var text = request.Comment?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        var details = request.Details is { IsEmpty: false } d ? d : null;
        if (request.Sentiment == null && request.Rating == null)
            return Invalid(details != null ? DetailsNeedSentiment : "Rate your experience with the seller or rate the item.");
        if (request.Sentiment is { } s && !Enum.IsDefined(s)) return Invalid("Choose Positive, Neutral or Negative for the seller.");
        if (details != null && request.Sentiment == null) return Invalid(DetailsNeedSentiment);
        if (details != null && new[] { details.Description, details.ShippingTime, details.Communication }
                .Any(x => x is { } v && (v < FeedbackLimits.MinDetailRating || v > FeedbackLimits.MaxDetailRating)))
            return Invalid("Rate each detail from 1 to 5 stars.");
        if (text != null && (text.Length > FeedbackLimits.MaxCommentLength || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'))))
            return Invalid($"Feedback must be at most {FeedbackLimits.MaxCommentLength} characters without control characters.");
        if (request.Rating is { } rating && ReviewService.Validate(rating, text, out _) is { } badReview) return Invalid(badReview.Error);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            int? orderId = null;
            if (request.Sentiment is { } sentiment)
            {
                var feedback = await Submit(buyerId, request.OrderItemId, sentiment, text, details, cancellationToken);
                if (feedback.Outcome != LeaveFeedbackOutcome.Success) return await Abort(transaction, feedback);
                orderId = feedback.OrderId;
            }
            if (request.Rating is { } stars)
            {
                var review = await new ReviewService(db).Submit(buyerId, request.OrderItemId, stars, text, cancellationToken);
                if (review.Outcome != ReviewOutcome.Success) return await Abort(transaction, new(Map(review.Outcome), Error: review.Error));
                orderId = review.OrderId;
            }
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(LeaveFeedbackOutcome.Success, orderId, request.Sentiment != null, request.Rating != null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return await Abort(transaction, new(LeaveFeedbackOutcome.Conflict, Error: "This feedback was already submitted."));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static LeaveFeedbackResult Invalid(string? error) => new(LeaveFeedbackOutcome.Invalid, Error: error);

    private static LeaveFeedbackOutcome Map(ReviewOutcome outcome) => outcome switch
    {
        ReviewOutcome.Invalid => LeaveFeedbackOutcome.Invalid,
        ReviewOutcome.NotFound => LeaveFeedbackOutcome.NotFound,
        ReviewOutcome.Forbidden => LeaveFeedbackOutcome.Forbidden,
        _ => LeaveFeedbackOutcome.Conflict
    };

    private async Task<LeaveFeedbackResult> Abort(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, LeaveFeedbackResult result)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return result;
    }

    private async Task<LeaveFeedbackResult> Submit(int buyerId, int orderItemId, FeedbackSentiment sentiment, string? comment, FeedbackDetails? details, CancellationToken ct)
    {
        // Lock order: User -> OrderTable -> OrderItem -> Payment (only once Delivered, when B06 can no longer write it) -> SellerFeedback.
        var orderId = await db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == orderItemId).Select(x => x.OrderId).SingleOrDefaultAsync(ct);
        var actor = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={buyerId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (actor?.Role != "Buyer") return new(LeaveFeedbackOutcome.Forbidden);
        if (orderId == null) return new(LeaveFeedbackOutcome.NotFound);
        var order = await db.Set<OrderTable>().FromSqlInterpolated($"SELECT * FROM dbo.OrderTable WITH (HOLDLOCK) WHERE id={orderId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (order == null || order.BuyerId != buyerId) return new(LeaveFeedbackOutcome.NotFound);
        var item = await db.Set<OrderItem>().FromSqlInterpolated($"SELECT * FROM dbo.OrderItem WITH (HOLDLOCK) WHERE id={orderItemId} AND orderId={order.Id}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (item == null) return new(LeaveFeedbackOutcome.NotFound);
        var deliveredAt = order.Status == Delivered
            ? DeliveredAt(await db.Set<Payment>().FromSqlInterpolated($"SELECT * FROM dbo.Payment WITH (HOLDLOCK, FORCESEEK(IX_Payment_Order(orderId))) WHERE orderId={order.Id}").AsNoTracking().Take(2).ToListAsync(ct))
            : null;
        var existing = await db.Set<SellerFeedback>().FromSqlInterpolated($"SELECT * FROM dbo.SellerFeedback WITH (UPDLOCK,HOLDLOCK) WHERE OrderItemId={orderItemId}")
            .AsNoTracking().Select(x => x.Sentiment).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var eligibility = Eligibility(item.Id, order, deliveredAt, existing, now);
        if (!eligibility.CanLeave) return new(LeaveFeedbackOutcome.Conflict, Error: eligibility.Reason);
        if (eligibility.PositiveOnly && sentiment != FeedbackSentiment.Positive) return new(LeaveFeedbackOutcome.Conflict, Error: WaitMessage);
        db.Add(new SellerFeedback
        {
            OrderItemId = item.Id, BuyerId = buyerId, SellerId = order.SellerId!.Value, Sentiment = sentiment.ToString(), Comment = comment, CreatedAtUtc = now,
            ArrivedOnTime = details?.ArrivedOnTime, DescriptionRating = (byte?)details?.Description,
            ShippingTimeRating = (byte?)details?.ShippingTime, CommunicationRating = (byte?)details?.Communication
        });
        await db.SaveChangesAsync(ct);
        return new(LeaveFeedbackOutcome.Success, order.Id);
    }
}
