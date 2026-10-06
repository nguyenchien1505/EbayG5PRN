using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EbayGroup5.Application.Checkout;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Checkout;

public sealed class CheckoutService(EbayDbContext db) : ICheckoutService
{
    private static bool Field(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static bool Usable(Address address) => Field(address.FullName, 100) && Field(address.Street, 100) && Field(address.City, 50) && Field(address.Country, 50);

    public async Task<IReadOnlyList<Address>> AddressesAsync(int buyerId, CancellationToken cancellationToken = default) =>
        await db.Set<Address>().AsNoTracking().Where(x => x.UserId == buyerId)
            .OrderByDescending(x => x.IsDefault == true).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<int?> AddAddressAsync(int buyerId, AddressInput input, CancellationToken cancellationToken = default)
    {
        if (!Field(input.FullName, 100) || !Field(input.Phone, 20) || !Field(input.Street, 100) ||
            !Field(input.City, 50) || !Field(input.Country, 50) || (input.State?.Trim().Length ?? 0) > 50) return null;
        if (!await db.Set<User>().AnyAsync(x => x.Id == buyerId && x.Role == "Buyer", cancellationToken)) return null;
        var address = new Address { UserId = buyerId, FullName = input.FullName.Trim(), Phone = input.Phone.Trim(),
            Street = input.Street.Trim(), City = input.City.Trim(), State = input.State?.Trim(), Country = input.Country.Trim(), IsDefault = false };
        db.Add(address);
        await db.SaveChangesAsync(cancellationToken);
        return address.Id;
    }

    public async Task<CheckoutResult> PlaceAsync(int buyerId, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ProductId <= 0 || request.AddressId <= 0 || request.Quantity is < 1 or > 99 || request.Key == Guid.Empty ||
            request.ExpectedUnitPrice < 0 || request.ExpectedUnitPrice > 99999999.99m || decimal.Round(request.ExpectedUnitPrice, 2) != request.ExpectedUnitPrice)
            return new(null, "Check your item, quantity and shipping address.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{request.ProductId}|{request.AddressId}|{request.Quantity}|{request.ExpectedUnitPrice:F2}"))));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // A Buyer gate serializes duplicate attempts; product gate serializes competing Buyers.
            var buyer = await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (UPDLOCK,HOLDLOCK) WHERE id={buyerId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (buyer?.Role != "Buyer") return new(null, "Buyer access is required.");
            var previous = await db.Set<OrderTable>().AsNoTracking().SingleOrDefaultAsync(x => x.BuyerId == buyerId && x.CheckoutKey == request.Key, cancellationToken);
            if (previous != null) return previous.CheckoutFingerprint == fingerprint
                ? new(previous.Id, null) : new(null, "This checkout request was already used. Start a new checkout.", true);
            var product = await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM dbo.Product WITH (UPDLOCK,HOLDLOCK) WHERE id={request.ProductId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var seller = product?.SellerId == null ? null : await db.Set<User>().FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH (HOLDLOCK) WHERE id={product.SellerId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (product == null || product.IsAuction != false || string.IsNullOrWhiteSpace(product.Title) || product.Price is null or < 0 || seller?.Role != "Seller")
                return new(null, "This item is no longer available.", true);
            var address = await db.Set<Address>().FromSqlInterpolated($"SELECT * FROM dbo.Address WITH (HOLDLOCK) WHERE id={request.AddressId} AND userId={buyerId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (address == null || !Usable(address)) return new(null, "Select a valid shipping address from your account.");
            if (product.Price != request.ExpectedUnitPrice) return new(null, "The price has changed. Review the current price and try again.", true);
            var total = product.Price.Value * request.Quantity;
            if (total > 99999999.99m) return new(null, "The order total exceeds the supported amount.");
            var inventory = await db.Set<Inventory>().FromSqlInterpolated($"SELECT * FROM dbo.Inventory WITH (UPDLOCK,HOLDLOCK) WHERE productId={request.ProductId}").OrderBy(x => x.Id).ToListAsync(cancellationToken);
            if (inventory.Sum(x => Math.Max(0L, x.Quantity ?? 0)) < request.Quantity) return new(null, "There is not enough stock for this quantity.", true);
            var remaining = request.Quantity;
            foreach (var row in inventory.Where(x => x.Quantity > 0))
            {
                var taken = Math.Min(remaining, row.Quantity!.Value);
                row.Quantity -= taken;
                row.LastUpdated = DateTime.UtcNow;
                remaining -= taken;
                if (remaining == 0) break;
            }
            var order = new OrderTable { BuyerId = buyerId, AddressId = address.Id, OrderDate = DateTime.UtcNow,
                TotalPrice = total, Status = "Pending", CheckoutKey = request.Key, CheckoutFingerprint = fingerprint };
            db.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            db.Add(new OrderItem { OrderId = order.Id, ProductId = product.Id, Quantity = request.Quantity, UnitPrice = product.Price });
            db.Add(new Payment { OrderId = order.Id, UserId = buyerId, Amount = total, Method = "COD", Status = "Pending", PaidAt = null });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(order.Id, null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<OrderConfirmation?> FindAsync(int buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Set<OrderTable>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId && x.CheckoutKey != null, cancellationToken);
        if (order == null) return null;
        var item = await db.Set<OrderItem>().AsNoTracking().SingleAsync(x => x.OrderId == orderId, cancellationToken);
        var payment = await db.Set<Payment>().AsNoTracking().SingleAsync(x => x.OrderId == orderId && x.UserId == buyerId, cancellationToken);
        var address = await db.Set<Address>().AsNoTracking().SingleAsync(x => x.Id == order.AddressId && x.UserId == buyerId, cancellationToken);
        return new(order.Id, order.TotalPrice!.Value, item.Quantity!.Value, item.UnitPrice!.Value, order.Status!, payment.Method!, payment.Status!, address);
    }
}
