using System.Data;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Orders;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Disputes;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Orders;

public sealed class OrderService(EbayDbContext db, TimeProvider? clock = null, DisputeTiming? timing = null) : IOrderService
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly DisputeTiming timing = timing ?? new();
    private static string Role(OrderAudience audience) => audience == OrderAudience.Buyer ? "Buyer" : "Seller";
    private IQueryable<OrderTable> Owned(int actorId, OrderAudience audience) => db.Set<OrderTable>().AsNoTracking()
        .Where(x => audience == OrderAudience.Buyer ? x.BuyerId == actorId : x.SellerId == actorId);
    private Task<bool> HasRole(int actorId, OrderAudience audience, CancellationToken ct) =>
        db.Set<User>().AnyAsync(x => x.Id == actorId && x.Role == Role(audience), ct);

    public async Task<OrderHistory> HistoryAsync(int actorId, OrderAudience audience, int page = 1, string? status = null, CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > int.MaxValue / OrderHistory.PageSize || !OrderHistory.IsValidFilter(status, audience))
            throw new ArgumentException("Invalid order filters.");
        if (!await HasRole(actorId, audience, cancellationToken)) throw new UnauthorizedAccessException();
        var buyer = audience == OrderAudience.Buyer;
        var due = FeedbackDue.Items(db, actorId, clock.GetUtcNow().UtcDateTime);
        if (!buyer) due = due.Where(_ => false); // Sellers never see buyer feedback actions.
        var orders = Owned(actorId, audience);
        if (status == OrderHistory.ReadyForFeedback) orders = orders.Where(x => due.Any(item => item.OrderId == x.Id));
        else if (status != null) orders = orders.Where(x => x.Status == status);
        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderByDescending(x => x.Id).Skip((page - 1) * OrderHistory.PageSize).Take(OrderHistory.PageSize)
            .Select(x => new
            {
                x.Id, x.OrderDate, x.Status, x.TotalPrice,
                Title = (from item in db.Set<OrderItem>() join product in db.Set<Product>() on item.ProductId equals product.Id
                    where item.OrderId == x.Id orderby item.Id select product.Title).FirstOrDefault(),
                ImageData = (from item in db.Set<OrderItem>() join product in db.Set<Product>() on item.ProductId equals product.Id
                    where item.OrderId == x.Id orderby item.Id select product.Images).FirstOrDefault(),
                Count = db.Set<OrderItem>().Count(item => item.OrderId == x.Id),
                FeedbackItemId = due.Where(item => item.OrderId == x.Id).OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefault(),
                FeedbackLines = due.Count(item => item.OrderId == x.Id),
                ContactItemId = buyer ? db.Set<OrderItem>().Where(item => item.OrderId == x.Id && item.ProductId != null)
                    .OrderBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefault() : null
            }).ToListAsync(cancellationToken);
        var summaries = rows.Select(x => new OrderSummary(x.Id, x.OrderDate, x.Status ?? "Unknown", x.TotalPrice, x.Title ?? "Item unavailable", x.Count)
            { FeedbackItemId = x.FeedbackItemId, FeedbackLines = x.FeedbackLines, ContactItemId = x.ContactItemId, Image = CatalogImages.Parse(x.ImageData).FirstOrDefault() }).ToList();
        return new(summaries, total, page, status, audience);
    }

    private sealed record State(OrderTable Order, List<OrderItem> Items, int ItemCount, Address? Address, List<Payment> Payments,
        List<ShippingInfo> Shipments, bool Common, bool CanShip, bool CanReceive, bool Delivered);

    private async Task<State> Load(OrderTable order, CancellationToken ct)
    {
        // Q-PERF-001 (LOAD.md): no range locks on the order's child rows. Every writer of OrderItem/Payment/ShippingInfo for this order
        // holds the order row's UPDLOCK first (ActAsync; checkout creates them with the order), so HOLDLOCK here only added gap locks that
        // deadlocked unrelated orders. FindAsync is a plain read.
        var items = await db.Set<OrderItem>().FromSqlInterpolated($"SELECT * FROM dbo.OrderItem WHERE orderId={order.Id}")
            .AsNoTracking().OrderBy(x => x.Id).Take(51).ToListAsync(ct);
        var itemCount = items.Count <= 50 ? items.Count : await db.Set<OrderItem>().CountAsync(x => x.OrderId == order.Id, ct);
        var address = await db.Set<Address>().FromSqlInterpolated($"SELECT * FROM dbo.Address WITH (HOLDLOCK) WHERE id={order.AddressId} AND userId={order.BuyerId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        var payments = await db.Set<Payment>().FromSqlInterpolated($"SELECT * FROM dbo.Payment WHERE orderId={order.Id}")
            .AsNoTracking().OrderBy(x => x.Id).Take(2).ToListAsync(ct);
        var shipments = await db.Set<ShippingInfo>().FromSqlInterpolated($"SELECT * FROM dbo.ShippingInfo WHERE orderId={order.Id}")
            .AsNoTracking().OrderBy(x => x.Id).Take(2).ToListAsync(ct);
        var seller = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={order.SellerId}").AsNoTracking().SingleOrDefaultAsync(ct);
        var buyer = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={order.BuyerId}").AsNoTracking().SingleOrDefaultAsync(ct);
        var sellerValid = seller?.Role == "Seller";
        var buyerValid = buyer?.Role == "Buyer";
        var common = buyerValid && sellerValid && address != null && !string.IsNullOrWhiteSpace(address.FullName) &&
            !string.IsNullOrWhiteSpace(address.Street) && !string.IsNullOrWhiteSpace(address.City) && !string.IsNullOrWhiteSpace(address.Country) &&
            order.TotalPrice is >= 0 && items.Count is > 0 and <= 50 && items.All(x => x.ProductId is > 0 && x.Quantity is > 0 && x.UnitPrice is >= 0) &&
            items.Sum(x => x.Quantity!.Value * x.UnitPrice!.Value) == order.TotalPrice && payments.Count == 1 &&
            payments[0].UserId == order.BuyerId && payments[0].Amount == order.TotalPrice && payments[0].Method == "COD";
        var pendingPayment = common && payments[0].Status == "Pending" && payments[0].PaidAt == null;
        var canShip = pendingPayment && order.Status == "Pending" && shipments.Count == 0;
        var canReceive = pendingPayment && order.Status == "Shipped" && shipments.Count == 1 && shipments[0].Status == "Shipped";
        var delivered = common && order.Status == "Delivered" && shipments.Count == 1 && shipments[0].Status == "Delivered" &&
            payments[0].Status == "Paid" && payments[0].PaidAt != null;
        return new(order, items, itemCount, address, payments, shipments, common, canShip, canReceive, delivered);
    }

    public async Task<OrderDetail?> FindAsync(int actorId, OrderAudience audience, int orderId, CancellationToken cancellationToken = default)
    {
        if (!await HasRole(actorId, audience, cancellationToken)) return null;
        var order = await Owned(actorId, audience).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null) return null;
        var state = await Load(order, cancellationToken);
        var productIds = state.Items.Take(50).Where(x => x.ProductId != null).Select(x => x.ProductId!.Value).Distinct().ToArray();
        var products = await db.Set<Product>().AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var lines = state.Items.Take(50).Select(x =>
        {
            var product = x.ProductId is int id ? products.GetValueOrDefault(id) : null;
            return new OrderLine(x.Id, x.ProductId, product?.Title ?? "Item unavailable", x.Quantity, x.UnitPrice)
                { Image = CatalogImages.Parse(product?.Images).FirstOrDefault() };
        }).ToList();
        var address = state.Address == null ? null : new DeliveryAddress(state.Address.FullName ?? "", state.Address.Phone,
            state.Address.Street ?? "", state.Address.City ?? "", state.Address.State, state.Address.Country ?? "");
        var payment = state.Payments.Count == 1 ? state.Payments[0] : null;
        var shipping = state.Shipments.Count == 1 ? state.Shipments[0] : null;
        var notice = state.CanShip || state.CanReceive || state.Delivered || order.Status == "Cancelled" ? null :
            "This order is read-only because its legacy details are incomplete or inconsistent. No records have been changed.";
        return new(order.Id, order.OrderDate, order.Status ?? "Unknown", order.TotalPrice, lines, state.ItemCount, address,
            payment?.Method, payment?.Status, payment?.PaidAt, shipping?.Carrier, shipping?.TrackingNumber,
            audience == OrderAudience.Seller && state.CanShip, audience == OrderAudience.Buyer && state.CanReceive, notice, audience)
            { EstimatedDeliveryUtc = order.EstimatedDeliveryUtc is { } estimated ? DateTime.SpecifyKind(estimated, DateTimeKind.Utc) : null };
    }

    public async Task<OrderActionResult> ActAsync(int actorId, OrderAction action, int orderId, CancellationToken cancellationToken = default)
    {
        if (action is not (OrderAction.Ship or OrderAction.Receive)) return new(OrderOutcome.Conflict, "Unsupported order action.");
        var audience = action == OrderAction.Ship ? OrderAudience.Seller : OrderAudience.Buyer;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var actor = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={actorId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (actor?.Role != Role(audience)) return new(OrderOutcome.Forbidden);
            var order = await db.Set<OrderTable>().FromSqlInterpolated($"SELECT * FROM dbo.OrderTable WITH (UPDLOCK,HOLDLOCK) WHERE id={orderId}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (order == null || (audience == OrderAudience.Seller ? order.SellerId != actorId : order.BuyerId != actorId)) return new(OrderOutcome.NotFound);
            var state = await Load(order, cancellationToken);
            if (state.Delivered || (action == OrderAction.Ship && state.CanReceive)) return new(OrderOutcome.Success);
            if (action == OrderAction.Ship ? !state.CanShip : !state.CanReceive)
                return new(OrderOutcome.Conflict, "This action is not available for the current order state. Review the order details.");
            db.Attach(order);
            if (action == OrderAction.Ship)
            {
                order.Status = "Shipped";
                db.Add(new ShippingInfo { OrderId = order.Id, Carrier = "Demo Carrier", TrackingNumber = "DEMO-B06-" + order.Id,
                    Status = "Shipped", EstimatedArrival = order.EstimatedDeliveryUtc });
            }
            else
            {
                order.Status = "Delivered";
                var shipping = state.Shipments[0];
                db.Attach(shipping);
                shipping.Status = "Delivered";
                var payment = state.Payments[0];
                db.Attach(payment);
                payment.Status = "Paid";
                var now = clock.GetUtcNow().UtcDateTime;
                payment.PaidAt = now;
                // E01: the item arrived, so open item-not-received requests close in the same transaction (Outcome ItemReceived).
                await DisputeService.ItemReceivedAsync(db, actorId, order.Id, now, this.timing, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(OrderOutcome.Success);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
