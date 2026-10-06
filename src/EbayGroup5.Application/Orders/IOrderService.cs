namespace EbayGroup5.Application.Orders;

public enum OrderAudience { Buyer, Seller }
public enum OrderAction { Ship, Receive }
public enum OrderOutcome { Success, NotFound, Forbidden, Conflict }
public sealed record OrderActionResult(OrderOutcome Outcome, string? Error = null);
public sealed record OrderSummary(int Id, DateTime? Date, string Status, decimal? Total, string Title, int ItemCount);
public sealed record OrderHistory(IReadOnlyList<OrderSummary> Orders, int Total, int Page, string? Status, OrderAudience Audience)
{
    public const int PageSize = 20;
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
public sealed record OrderLine(int Id, int? ProductId, string Title, int? Quantity, decimal? UnitPrice);
public sealed record DeliveryAddress(string FullName, string? Phone, string Street, string City, string? State, string Country);
public sealed record OrderDetail(int Id, DateTime? Date, string Status, decimal? Total, IReadOnlyList<OrderLine> Lines, int ItemCount,
    DeliveryAddress? Address, string? PaymentMethod, string? PaymentStatus, DateTime? PaidAt, string? Carrier, string? Tracking,
    bool CanShip, bool CanReceive, string? Notice, OrderAudience Audience);

public interface IOrderService
{
    Task<OrderHistory> HistoryAsync(int actorId, OrderAudience audience, int page = 1, string? status = null, CancellationToken cancellationToken = default);
    Task<OrderDetail?> FindAsync(int actorId, OrderAudience audience, int orderId, CancellationToken cancellationToken = default);
    Task<OrderActionResult> ActAsync(int actorId, OrderAction action, int orderId, CancellationToken cancellationToken = default);
}
