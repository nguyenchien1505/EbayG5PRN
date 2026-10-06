using System.Data;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Reviews;

public sealed class ReviewService(EbayDbContext db) : IReviewService
{
    private const string Delivered = "Delivered";

    // Single visibility rule for public reads: order-linked (Q-BIZ-002) and not hidden by moderation (D05).
    private IQueryable<Review> Visible() =>
        db.Set<Review>().AsNoTracking().Where(x => x.ProductId != null && x.OrderItemId != null && x.HiddenAtUtc == null
            && x.Rating >= ReviewLimits.MinRating && x.Rating <= ReviewLimits.MaxRating);
    private IQueryable<Review> Visible(int productId) => Visible().Where(x => x.ProductId == productId);

    private sealed record RatingGroup(int Rating, int Count, int Reviews);

    // Shared by the product page and catalog cards so both always show the same numbers.
    private static RatingSummary Summarize(IReadOnlyCollection<RatingGroup> groups)
    {
        var ratings = groups.Sum(x => x.Count);
        return ratings == 0 ? RatingSummary.Empty : new RatingSummary(ratings, groups.Sum(x => x.Reviews),
            Math.Round(groups.Sum(x => (decimal)x.Rating * x.Count) / ratings, 1, MidpointRounding.AwayFromZero),
            Enumerable.Range(1, 5).Reverse().Select(star => groups.Where(x => x.Rating == star).Sum(x => x.Count)).ToList());
    }

    public async Task<IReadOnlyDictionary<int, RatingSummary>> SummariesAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length > ReviewLimits.MaxBatchIds) throw new ArgumentException("Too many products.", nameof(productIds));
        if (ids.Length == 0) return new Dictionary<int, RatingSummary>();
        var rows = await Visible().Where(x => ids.Contains(x.ProductId!.Value))
            .GroupBy(x => new { Product = x.ProductId!.Value, Rating = x.Rating!.Value })
            .Select(g => new { g.Key.Product, g.Key.Rating, Count = g.Count(), Reviews = g.Count(x => x.Comment != null && x.CommentRemovedAtUtc == null) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(x => x.Product).ToDictionary(g => g.Key, g => Summarize(g.Select(x => new RatingGroup(x.Rating, x.Count, x.Reviews)).ToList()));
    }

    public async Task<IReadOnlyList<ReviewLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        if (!await db.Set<User>().AnyAsync(x => x.Id == buyerId && x.Role == "Buyer", cancellationToken)) return null;
        var order = await db.Set<OrderTable>().AsNoTracking().Where(x => x.Id == orderId && x.BuyerId == buyerId)
            .Select(x => new { x.Status }).SingleOrDefaultAsync(cancellationToken);
        if (order == null) return null;
        return await Lines(db.Set<OrderItem>().AsNoTracking().Where(x => x.OrderId == orderId), order.Status == Delivered, cancellationToken);
    }

    public async Task<ReviewItemContext?> FindItemAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default)
    {
        if (!await db.Set<User>().AnyAsync(x => x.Id == buyerId && x.Role == "Buyer", cancellationToken)) return null;
        var order = await (from item in db.Set<OrderItem>().AsNoTracking()
                           join o in db.Set<OrderTable>().AsNoTracking() on item.OrderId equals o.Id
                           where item.Id == orderItemId && o.BuyerId == buyerId
                           select new { o.Id, o.Status }).SingleOrDefaultAsync(cancellationToken);
        if (order == null) return null;
        var lines = await Lines(db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == orderItemId), order.Status == Delivered, cancellationToken);
        return new(order.Id, lines.Single());
    }

    private async Task<IReadOnlyList<ReviewLineEligibility>> Lines(IQueryable<OrderItem> items, bool delivered, CancellationToken ct)
    {
        var lines = await (from item in items
                           orderby item.Id
                           select new
                           {
                               item.Id, item.ProductId,
                               Title = db.Set<Product>().Where(p => p.Id == item.ProductId).Select(p => p.Title).FirstOrDefault(),
                               Review = db.Set<Review>().Where(r => r.OrderItemId == item.Id).Select(r => new { r.Id, r.Rating }).FirstOrDefault()
                           }).Take(ReviewLimits.MaxLines).ToListAsync(ct);
        return lines.Select(x =>
        {
            var reviewed = x.Review != null;
            var reason = reviewed ? "You already reviewed this item." : x.ProductId == null ? "This item is no longer available to review." :
                !delivered ? "You can review this item after the order is delivered." : null;
            return new ReviewLineEligibility(x.Id, x.ProductId, x.Title ?? "Item unavailable", reason == null, reviewed, x.Review?.Id, reason, x.Review?.Rating);
        }).ToList();
    }

    public async Task<ProductReviewPage> ProductReviewsAsync(int productId, int page = 1, ReviewSort sort = ReviewSort.Recent, CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > int.MaxValue / ProductReviewPage.PageSize || !Enum.IsDefined(sort)) throw new ArgumentException("Invalid review filters.");
        var groups = await Visible(productId).GroupBy(x => x.Rating!.Value)
            .Select(g => new RatingGroup(g.Key, g.Count(), g.Count(x => x.Comment != null && x.CommentRemovedAtUtc == null))).ToListAsync(cancellationToken);
        var summary = Summarize(groups);
        var withComment = Visible(productId).Where(x => x.Comment != null && x.CommentRemovedAtUtc == null);
        var ordered = sort switch
        {
            ReviewSort.Highest => withComment.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Id),
            ReviewSort.Lowest => withComment.OrderBy(x => x.Rating).ThenByDescending(x => x.Id),
            _ => withComment.OrderByDescending(x => x.Id)
        };
        var reviews = await ordered.Skip((page - 1) * ProductReviewPage.PageSize).Take(ProductReviewPage.PageSize)
            .Select(x => new ProductReview(x.Id, db.Set<User>().Where(u => u.Id == x.ReviewerId).Select(u => u.Username).FirstOrDefault() ?? "eBay user",
                x.Rating!.Value, x.Comment!, x.CreatedAt!.Value, x.ReviewerId!.Value)).ToListAsync(cancellationToken);
        return new(summary, reviews, page, sort);
    }

    public async Task<ReviewResult> SubmitAsync(int buyerId, int orderItemId, int rating, string? comment, CancellationToken cancellationToken = default)
    {
        if (Validate(rating, comment, out var text) is { } invalid) return invalid;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var result = await Submit(buyerId, orderItemId, rating, text, cancellationToken);
            if (result.Outcome != ReviewOutcome.Success)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return result;
            }
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new(ReviewOutcome.Conflict, Error: "You already reviewed this item.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    internal static ReviewResult? Validate(int rating, string? comment, out string? text)
    {
        text = comment?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        if (rating is < ReviewLimits.MinRating or > ReviewLimits.MaxRating) return new(ReviewOutcome.Invalid, Error: "Rating must be from 1 to 5.");
        if (text != null && (text.Length > ReviewLimits.MaxCommentLength || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'))))
            return new(ReviewOutcome.Invalid, Error: $"Comment must be at most {ReviewLimits.MaxCommentLength} characters without control characters.");
        return null;
    }

    // Caller owns the transaction; input must already pass Validate. Also used by the combined R1 submission in FeedbackService.
    internal async Task<ReviewResult> Submit(int buyerId, int orderItemId, int rating, string? comment, CancellationToken ct)
    {
        // Same lock order as B06 (User -> OrderTable -> OrderItem); shared HOLDLOCK keeps the order status stable until commit.
        var orderId = await db.Set<OrderItem>().AsNoTracking().Where(x => x.Id == orderItemId).Select(x => x.OrderId).SingleOrDefaultAsync(ct);
        var actor = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={buyerId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (actor?.Role != "Buyer") return new(ReviewOutcome.Forbidden);
        if (orderId == null) return new(ReviewOutcome.NotFound);
        var order = await db.Set<OrderTable>().FromSqlInterpolated($"SELECT * FROM dbo.OrderTable WITH (HOLDLOCK) WHERE id={orderId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (order == null || order.BuyerId != buyerId) return new(ReviewOutcome.NotFound);
        var item = await db.Set<OrderItem>().FromSqlInterpolated($"SELECT * FROM dbo.OrderItem WITH (HOLDLOCK) WHERE id={orderItemId} AND orderId={order.Id}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (item == null) return new(ReviewOutcome.NotFound);
        if (order.Status != Delivered) return new(ReviewOutcome.Conflict, Error: "You can review this item after the order is delivered.");
        if (item.ProductId == null) return new(ReviewOutcome.Conflict, Error: "This item is no longer available to review.");
        var existing = await db.Set<Review>().FromSqlInterpolated($"SELECT * FROM dbo.Review WITH (UPDLOCK,HOLDLOCK) WHERE OrderItemId={orderItemId}").AsNoTracking().AnyAsync(ct);
        if (existing) return new(ReviewOutcome.Conflict, Error: "You already reviewed this item.");
        var review = new Review { ProductId = item.ProductId, ReviewerId = buyerId, OrderItemId = item.Id, Rating = rating, Comment = comment, CreatedAt = DateTime.UtcNow };
        db.Add(review);
        await db.SaveChangesAsync(ct);
        return new(ReviewOutcome.Success, review.Id, order.Id);
    }
}
