namespace EbayGroup5.Application.Orders;

public enum OrderAudience { Buyer, Seller }
public enum OrderAction { Ship, Receive }
public enum OrderOutcome { Success, NotFound, Forbidden, Conflict }
public sealed record OrderActionResult(OrderOutcome Outcome, string? Error = null);
public sealed record OrderSummary(int Id, DateTime? Date, string Status, decimal? Total, string Title, int ItemCount)
{
    /// <summary>Buyer only (D06): first line whose Leave feedback button is open on order details, and how many such lines.</summary>
    public int? FeedbackItemId { get; init; }
    public int FeedbackLines { get; init; }
    /// <summary>Buyer only: first line that still has a product, used by More actions → Contact seller.</summary>
    public int? ContactItemId { get; init; }
    /// <summary>H06b: first picture of the first line's product (CatalogImages rules), null when there is none.</summary>
    public string? Image { get; init; }
}
public sealed record OrderHistory(IReadOnlyList<OrderSummary> Orders, int Total, int Page, string? Status, OrderAudience Audience)
{
    public const int PageSize = 20;
    /// <summary>Buyer-only filter value (eBay "Ready for feedback").</summary>
    public const string ReadyForFeedback = "ReadyForFeedback";
    public static readonly IReadOnlyList<string> Statuses = ["Pending", "Shipped", "Delivered", "Cancelled"];
    public static bool IsValidFilter(string? status, OrderAudience audience) =>
        status == null || Statuses.Contains(status) || (status == ReadyForFeedback && audience == OrderAudience.Buyer);
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
public sealed record OrderLine(int Id, int? ProductId, string Title, int? Quantity, decimal? UnitPrice)
{
    /// <summary>H06b: first product picture (CatalogImages rules), null when there is none.</summary>
    public string? Image { get; init; }
}
public sealed record DeliveryAddress(string FullName, string? Phone, string Street, string City, string? State, string Country);
public sealed record OrderDetail(int Id, DateTime? Date, string Status, decimal? Total, IReadOnlyList<OrderLine> Lines, int ItemCount,
    DeliveryAddress? Address, string? PaymentMethod, string? PaymentStatus, DateTime? PaidAt, string? Carrier, string? Tracking,
    bool CanShip, bool CanReceive, string? Notice, OrderAudience Audience)
{
    /// <summary>Latest estimated delivery (E01); null for legacy orders without an order date.</summary>
    public DateTime? EstimatedDeliveryUtc { get; init; }
}

public interface IOrderService
{
    Task<OrderHistory> HistoryAsync(int actorId, OrderAudience audience, int page = 1, string? status = null, CancellationToken cancellationToken = default);
    Task<OrderDetail?> FindAsync(int actorId, OrderAudience audience, int orderId, CancellationToken cancellationToken = default);
    Task<OrderActionResult> ActAsync(int actorId, OrderAction action, int orderId, CancellationToken cancellationToken = default);
}
