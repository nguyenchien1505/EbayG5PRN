using System.Data;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Infrastructure.Reviews;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Reputation;

public sealed class FeedbackService(EbayDbContext db, TimeProvider clock) : IFeedbackService
{
    private const string Delivered = "Delivered";
    private const string WaitMessage = "You need to wait 7 days before you can leave neutral or negative feedback for this seller.";
    private const string AlreadyLeft = "You already left feedback for this seller.";

    // Single visibility rule for reputation; D05 adds moderation here.
    private IQueryable<SellerFeedback> Visible() => db.Set<SellerFeedback>().AsNoTracking();

    public async Task<SellerReputation> ReputationAsync(int sellerId, CancellationToken cancellationToken = default)
    {
        var counts = await Visible().Where(x => x.SellerId == sellerId).GroupBy(x => x.Sentiment)
            .Select(g => new { Sentiment = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Sentiment, x => x.Count, cancellationToken);
        int Count(FeedbackSentiment sentiment) => counts.GetValueOrDefault(sentiment.ToString());
        return new(Count(FeedbackSentiment.Positive), Count(FeedbackSentiment.Neutral), Count(FeedbackSentiment.Negative));
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
        var sellerText = request.SellerComment?.Trim();
        if (string.IsNullOrEmpty(sellerText)) sellerText = null;
        var itemText = request.Comment?.Trim();
        if (string.IsNullOrEmpty(itemText)) itemText = null;
        if (request.Sentiment == null && request.Rating == null)
            return Invalid(itemText != null ? "Select a star rating from 1 to 5." : sellerText != null ? "Choose Positive, Neutral or Negative for the seller." : "Rate your experience with the seller or rate the item.");
        if (request.Rating == null && itemText != null) return Invalid("Select a star rating from 1 to 5.");
        if (request.Sentiment == null && sellerText != null) return Invalid("Choose Positive, Neutral or Negative for the seller.");
        if (request.Sentiment is { } s && !Enum.IsDefined(s)) return Invalid("Choose Positive, Neutral or Negative for the seller.");
        if (sellerText != null && (sellerText.Length > FeedbackLimits.MaxCommentLength || sellerText.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'))))
            return Invalid($"Seller feedback must be at most {FeedbackLimits.MaxCommentLength} characters without control characters.");
        if (request.Rating is { } rating && ReviewService.Validate(rating, request.Comment, out _) is { } badReview) return Invalid(badReview.Error);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            int? orderId = null;
            if (request.Sentiment is { } sentiment)
            {
                var feedback = await Submit(buyerId, request.OrderItemId, sentiment, sellerText, cancellationToken);
                if (feedback.Outcome != LeaveFeedbackOutcome.Success) return await Abort(transaction, feedback);
                orderId = feedback.OrderId;
            }
            if (request.Rating is { } stars)
            {
                var review = await new ReviewService(db).Submit(buyerId, request.OrderItemId, stars, itemText, cancellationToken);
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

    private async Task<LeaveFeedbackResult> Submit(int buyerId, int orderItemId, FeedbackSentiment sentiment, string? comment, CancellationToken ct)
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
            ? DeliveredAt(await db.Set<Payment>().FromSqlInterpolated($"SELECT * FROM dbo.Payment WITH (HOLDLOCK) WHERE orderId={order.Id}").AsNoTracking().Take(2).ToListAsync(ct))
            : null;
        var existing = await db.Set<SellerFeedback>().FromSqlInterpolated($"SELECT * FROM dbo.SellerFeedback WITH (UPDLOCK,HOLDLOCK) WHERE OrderItemId={orderItemId}")
            .AsNoTracking().Select(x => x.Sentiment).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var eligibility = Eligibility(item.Id, order, deliveredAt, existing, now);
        if (!eligibility.CanLeave) return new(LeaveFeedbackOutcome.Conflict, Error: eligibility.Reason);
        if (eligibility.PositiveOnly && sentiment != FeedbackSentiment.Positive) return new(LeaveFeedbackOutcome.Conflict, Error: WaitMessage);
        db.Add(new SellerFeedback { OrderItemId = item.Id, BuyerId = buyerId, SellerId = order.SellerId!.Value, Sentiment = sentiment.ToString(), Comment = comment, CreatedAtUtc = now });
        await db.SaveChangesAsync(ct);
        return new(LeaveFeedbackOutcome.Success, order.Id);
    }
}
