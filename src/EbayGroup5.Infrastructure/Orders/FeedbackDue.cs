using EbayGroup5.Application.Feedback;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Orders;

/// <summary>
/// D06 "Ready for feedback": order lines whose Leave feedback button is open on order details,
/// i.e. ReviewService.CanReview (D01) or FeedbackService.CanLeave (D03). Keep in sync with both.
/// </summary>
internal static class FeedbackDue
{
    private const string Delivered = "Delivered";

    internal static IQueryable<OrderItem> Items(EbayDbContext db, int buyerId, DateTime nowUtc)
    {
        var deliveredSince = nowUtc - FeedbackLimits.Window;
        return from item in db.Set<OrderItem>().AsNoTracking()
               join order in db.Set<OrderTable>().AsNoTracking() on item.OrderId equals order.Id
               where order.BuyerId == buyerId && order.Status == Delivered && (
                   // Product review still open: line has a product and no review yet.
                   (item.ProductId != null && !db.Set<Review>().Any(r => r.OrderItemId == item.Id)) ||
                   // Seller feedback still open: seller known, no feedback yet, exactly one Paid payment within the 60-day window.
                   (order.SellerId != null && !db.Set<SellerFeedback>().Any(f => f.OrderItemId == item.Id) &&
                    db.Set<Payment>().Count(p => p.OrderId == order.Id) == 1 &&
                    db.Set<Payment>().Any(p => p.OrderId == order.Id && p.Status == "Paid" && p.PaidAt != null && p.PaidAt >= deliveredSince)))
               select item;
    }
}
