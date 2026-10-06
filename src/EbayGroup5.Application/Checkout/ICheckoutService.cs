using EbayGroup5.Core.Entities;

namespace EbayGroup5.Application.Checkout;

public sealed record AddressInput(string FullName, string Phone, string Street, string City, string? State, string Country);
public sealed record CheckoutRequest(int ProductId, int AddressId, int Quantity, decimal ExpectedUnitPrice, Guid Key);
public sealed record CheckoutResult(int? OrderId, string? Error, bool Conflict = false);
public sealed record OrderConfirmation(int Id, decimal Total, int Quantity, decimal UnitPrice, string Status, string PaymentMethod, string PaymentStatus, Address Address);

public interface ICheckoutService
{
    Task<IReadOnlyList<Address>> AddressesAsync(int buyerId, CancellationToken cancellationToken = default);
    Task<int?> AddAddressAsync(int buyerId, AddressInput input, CancellationToken cancellationToken = default);
    Task<CheckoutResult> PlaceAsync(int buyerId, CheckoutRequest request, CancellationToken cancellationToken = default);
    Task<OrderConfirmation?> FindAsync(int buyerId, int orderId, CancellationToken cancellationToken = default);
}
