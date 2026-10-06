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

    public async Task<IReadOnlyList<ReviewLineEligibility>?> ListForOrderAsync(int buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        if (!await db.Set<User>().AnyAsync(x => x.Id == buyerId && x.Role == "Buyer", cancellationToken)) return null;
        var order = await db.Set<OrderTable>().AsNoTracking().Where(x => x.Id == orderId && x.BuyerId == buyerId)
            .Select(x => new { x.Status }).SingleOrDefaultAsync(cancellationToken);
        if (order == null) return null;
        var delivered = order.Status == Delivered;
        var lines = await (from item in db.Set<OrderItem>().AsNoTracking()
                           where item.OrderId == orderId
                           orderby item.Id
                           select new
                           {
                               item.Id, item.ProductId,
                               Title = db.Set<Product>().Where(p => p.Id == item.ProductId).Select(p => p.Title).FirstOrDefault(),
                               ReviewId = db.Set<Review>().Where(r => r.OrderItemId == item.Id).Select(r => (int?)r.Id).FirstOrDefault()
                           }).Take(ReviewLimits.MaxLines).ToListAsync(cancellationToken);
        return lines.Select(x =>
        {
            var reviewed = x.ReviewId != null;
            var reason = reviewed ? "You already reviewed this item." : x.ProductId == null ? "This item is no longer available to review." :
                !delivered ? "You can review this item after the order is delivered." : null;
            return new ReviewLineEligibility(x.Id, x.ProductId, x.Title ?? "Item unavailable", reason == null, reviewed, x.ReviewId, reason);
        }).ToList();
    }

    public async Task<ReviewResult> SubmitAsync(int buyerId, int orderItemId, int rating, string? comment, CancellationToken cancellationToken = default)
    {
        if (rating is < ReviewLimits.MinRating or > ReviewLimits.MaxRating) return new(ReviewOutcome.Invalid, Error: "Rating must be from 1 to 5.");
        var text = comment?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        if (text != null && (text.Length > ReviewLimits.MaxCommentLength || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'))))
            return new(ReviewOutcome.Invalid, Error: $"Comment must be at most {ReviewLimits.MaxCommentLength} characters without control characters.");

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

    private async Task<ReviewResult> Submit(int buyerId, int orderItemId, int rating, string? comment, CancellationToken ct)
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
