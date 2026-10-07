using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Orders;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using EbayGroup5.Infrastructure.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>
/// Q-PERF-001 (LOAD.md): F05 found checkout/ship/receive deadlocking when different orders are written at the same time (range locks
/// on Payment/ShippingInfo/Dispute by orderId). Every order here belongs to one worker, so any failure is a deadlock between unrelated orders.
/// </summary>
public sealed class OrderConcurrencyTests
{
    [Fact]
    public async Task Unrelated_orders_checkout_ship_and_receive_concurrently_without_deadlocks()
    {
        var s = await Setup();
        var demoSeller = int.Parse(SqlFixture.Scalar(s.Target, $"SELECT sellerId FROM dbo.Product WHERE id={s.OtherProduct}"));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Inventory SET quantity=100000 WHERE productId IN ({s.Product},{s.OtherProduct})");
        int otherAddress;
        await using (var db = Open(s.Target))
            otherAddress = (await new CheckoutService(db).AddAddressAsync(s.OtherBuyer, new("Other Buyer", "0123456789", "Second street", "City", null, "Vietnam")))!.Value;
        // Existing requests so the item-not-received lookup on Receive has rows to (wrongly) range-lock past.
        await Deliver(s, s.Order);
        await using (var db = Open(s.Target))
            Assert.Equal(DisputeResultCode.Success, (await new DisputeService(db, TimeProvider.System, new DisputeTiming())
                .OpenAsync(s.Buyer, new(s.Item, DisputeKind.Return, DisputeReason.Damaged, null, "Cracked."))).Code);

        var delivered = int.Parse(SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE status=N'Delivered'"));
        var lines = new[] { (Buyer: s.Buyer, Address: s.Address), (Buyer: s.OtherBuyer, Address: otherAddress) };
        var products = new[] { (Id: s.Product, Seller: s.Seller, Price: 49.90m), (Id: s.OtherProduct, Seller: demoSeller, Price: 24.50m) };
        var deadlocks = 0;
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 8), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (worker, ct) =>
        {
            for (var n = 0; n < 25; n++)
            {
                var (buyer, address) = lines[(worker + n) % 2];
                var product = products[(worker / 2 + n) % 2];
                try
                {
                    int order;
                    await using (var db = Open(s.Target))
                        order = (await new CheckoutService(db).PlaceAsync(buyer, new(product.Id, address, 1, product.Price, Guid.NewGuid()), ct)).OrderId
                            ?? throw new InvalidOperationException("checkout refused");
                    await using (var db = Open(s.Target))
                        Assert.Equal(OrderOutcome.Success, (await new OrderService(db).ActAsync(product.Seller, OrderAction.Ship, order, ct)).Outcome);
                    await using (var db = Open(s.Target))
                        Assert.Equal(OrderOutcome.Success, (await new OrderService(db).ActAsync(buyer, OrderAction.Receive, order, ct)).Outcome);
                }
                catch (Exception ex)
                {
                    if (Deadlock(ex)) Interlocked.Increment(ref deadlocks);
                    else failures.Add(ex.GetType().Name + ": " + ex.Message);
                }
            }
        });
        Assert.Empty(failures);
        Assert.Equal(0, deadlocks);
        Assert.Equal((delivered + 200).ToString(), SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE status=N'Delivered'"));
    }

    private static bool Deadlock(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number == 1205)) return true;
        return false;
    }
}
