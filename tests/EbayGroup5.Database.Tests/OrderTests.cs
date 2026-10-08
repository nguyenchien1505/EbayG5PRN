using System.Net;
using System.Text.RegularExpressions;
using DbUp.Engine;
using EbayGroup5.Application.Orders;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Orders;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class OrderTests
{
    private static EbayDbContext Open(string target) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
    private sealed record SetupData(string Target, int Buyer, int Seller, int OtherBuyer, int OtherSeller, int Order, int Product);
    private static async Task<SetupData> Setup()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        await using var db = Open(target);
        var accounts = new AccountService(db);
        foreach (var name in new[] { "buyer", "seller", "otherbuyer", "otherseller" })
            Assert.True(await accounts.RegisterAsync(name, name + "@b06.example.invalid", "Orders-Test-Password!"));
        var users = await db.Set<User>().Where(x => x.Email!.EndsWith("@B06.EXAMPLE.INVALID")).ToDictionaryAsync(x => x.Username!);
        foreach (var name in new[] { "seller", "otherseller" }) users[name].Role = "Seller";
        var product = await db.Set<Product>().SingleAsync(x => x.Title == "Wireless Headphones");
        product.SellerId = users["seller"].Id;
        await db.SaveChangesAsync();
        var service = new CheckoutService(db);
        var address = await service.AddAddressAsync(users["buyer"].Id, new("Test Buyer", "0123456789", "Example street", "Example city", null, "Vietnam"));
        var result = await service.PlaceAsync(users["buyer"].Id, new(product.Id, address!.Value, 2, 49.90m, Guid.NewGuid()));
        return new(target, users["buyer"].Id, users["seller"].Id, users["otherbuyer"].Id, users["otherseller"].Id, result.OrderId!.Value, product.Id);
    }

    [Fact]
    public async Task Buyer_and_seller_finish_order_without_changing_stock_and_retries_are_idempotent()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var service = new OrderService(db);
        Assert.Single((await service.HistoryAsync(s.Buyer, OrderAudience.Buyer)).Orders);
        Assert.Single((await service.HistoryAsync(s.Seller, OrderAudience.Seller)).Orders);
        Assert.True((await service.FindAsync(s.Seller, OrderAudience.Seller, s.Order))!.CanShip);
        Assert.Equal(OrderOutcome.Conflict, (await service.ActAsync(s.Buyer, OrderAction.Receive, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
        var shipped = await service.FindAsync(s.Buyer, OrderAudience.Buyer, s.Order);
        Assert.True(shipped!.CanReceive);
        Assert.Equal("Pending", shipped.PaymentStatus);
        Assert.Null(shipped.PaidAt);
        Assert.Equal("DEMO-B06-" + s.Order, shipped.Tracking);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Buyer, OrderAction.Receive, s.Order)).Outcome);
        var delivered = await service.FindAsync(s.Buyer, OrderAudience.Buyer, s.Order);
        Assert.Equal("Delivered", delivered!.Status);
        Assert.Equal("Paid", delivered.PaymentStatus);
        Assert.NotNull(delivered.PaidAt);
        Assert.False(delivered.CanReceive);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Buyer, OrderAction.Receive, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
        Assert.Equal(delivered.PaidAt, (await service.FindAsync(s.Buyer, OrderAudience.Buyer, s.Order))!.PaidAt);
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        Assert.Equal("Delivered", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        Assert.Equal("18", SqlFixture.Scalar(s.Target, $"SELECT quantity FROM dbo.Inventory WHERE productId={s.Product}"));
        Assert.Empty((await service.HistoryAsync(s.Seller, OrderAudience.Seller, status: "Pending")).Orders);
    }

    [Fact]
    public async Task Ownership_uses_seller_snapshot_and_price_snapshot_after_listing_changes()
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Product SET sellerId={s.OtherSeller}, price=1, isAuction=1 WHERE id={s.Product}");
        await using var db = Open(s.Target);
        var service = new OrderService(db);
        Assert.Empty((await service.HistoryAsync(s.OtherSeller, OrderAudience.Seller)).Orders);
        Assert.Null(await service.FindAsync(s.OtherSeller, OrderAudience.Seller, s.Order));
        Assert.Null(await service.FindAsync(s.OtherBuyer, OrderAudience.Buyer, s.Order));
        Assert.Null(await service.FindAsync(s.Buyer, OrderAudience.Buyer, int.MaxValue));
        Assert.Equal(OrderOutcome.NotFound, (await service.ActAsync(s.OtherSeller, OrderAction.Ship, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.NotFound, (await service.ActAsync(s.OtherBuyer, OrderAction.Receive, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.Forbidden, (await service.ActAsync(s.Buyer, OrderAction.Ship, s.Order)).Outcome);
        Assert.Equal(OrderOutcome.Forbidden, (await service.ActAsync(s.Seller, OrderAction.Receive, s.Order)).Outcome);
        Assert.Equal(99.80m, (await service.FindAsync(s.Seller, OrderAudience.Seller, s.Order))!.Total);
        Assert.Equal(49.90m, Assert.Single((await service.FindAsync(s.Buyer, OrderAudience.Buyer, s.Order))!.Lines).UnitPrice);
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    [InlineData("missingSeller")]
    [InlineData("paymentAmount")]
    [InlineData("duplicatePayment")]
    [InlineData("foreignAddress")]
    [InlineData("invalidLine")]
    [InlineData("shipment")]
    [InlineData("wrongMethod")]
    [InlineData("missingProduct")]
    public async Task Malformed_or_ineligible_orders_fail_closed_without_mutations(string kind)
    {
        var s = await Setup();
        var sql = kind switch
        {
            "cancelled" => $"UPDATE dbo.OrderTable SET status='Cancelled' WHERE id={s.Order}",
            "unknown" => $"UPDATE dbo.OrderTable SET status='Placed' WHERE id={s.Order}",
            "missingSeller" => $"UPDATE dbo.OrderTable SET SellerId=NULL WHERE id={s.Order}",
            "paymentAmount" => $"UPDATE dbo.Payment SET amount=1 WHERE orderId={s.Order}",
            "duplicatePayment" => $"INSERT dbo.Payment(orderId,userId,amount,method,status) VALUES({s.Order},{s.Buyer},99.80,'COD','Pending')",
            "foreignAddress" => $"UPDATE dbo.Address SET userId={s.OtherBuyer} WHERE id=(SELECT addressId FROM dbo.OrderTable WHERE id={s.Order})",
            "invalidLine" => $"UPDATE dbo.OrderItem SET quantity=NULL WHERE orderId={s.Order}",
            "shipment" => $"INSERT dbo.ShippingInfo(orderId,status) VALUES({s.Order},'Shipped')",
            "missingProduct" => $"UPDATE dbo.OrderItem SET productId=NULL WHERE orderId={s.Order}",
            _ => $"UPDATE dbo.Payment SET method='Card' WHERE orderId={s.Order}"
        };
        SqlFixture.Execute(s.Target, sql);
        var originalStatus = SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.OrderTable WHERE id={s.Order}");
        await using var db = Open(s.Target);
        var result = await new OrderService(db).ActAsync(s.Seller, OrderAction.Ship, s.Order);
        Assert.Equal(kind == "missingSeller" ? OrderOutcome.NotFound : OrderOutcome.Conflict, result.Outcome);
        Assert.Equal(originalStatus, SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.OrderTable WHERE id={s.Order}"));
        Assert.Equal(kind == "shipment" ? "1" : "0", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        Assert.Equal("18", SqlFixture.Scalar(s.Target, $"SELECT quantity FROM dbo.Inventory WHERE productId={s.Product}"));
    }

    [Theory]
    [InlineData(OrderAction.Ship)]
    [InlineData(OrderAction.Receive)]
    public async Task Concurrent_same_action_creates_one_shipment_and_one_paid_timestamp(OrderAction action)
    {
        var s = await Setup();
        await using var first = Open(s.Target);
        await using var second = Open(s.Target);
        if (action == OrderAction.Receive) await new OrderService(first).ActAsync(s.Seller, OrderAction.Ship, s.Order);
        var actor = action == OrderAction.Ship ? s.Seller : s.Buyer;
        var results = await Task.WhenAll(new OrderService(first).ActAsync(actor, action, s.Order), new OrderService(second).ActAsync(actor, action, s.Order));
        Assert.All(results, x => Assert.Equal(OrderOutcome.Success, x.Outcome));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        Assert.Equal(action == OrderAction.Ship ? "Shipped" : "Delivered", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.OrderTable WHERE id={s.Order}"));
        Assert.Equal(action == OrderAction.Ship ? "Pending" : "Paid", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.Payment WHERE orderId={s.Order}"));
    }

    [Theory]
    [InlineData(OrderAction.Ship)]
    [InlineData(OrderAction.Receive)]
    public async Task Failure_during_fulfillment_rolls_back_order_shipping_payment_and_allows_retry(OrderAction action)
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var service = new OrderService(db);
        if (action == OrderAction.Receive) await service.ActAsync(s.Seller, OrderAction.Ship, s.Order);
        var table = action == OrderAction.Ship ? "ShippingInfo" : "Payment";
        var triggerEvent = action == OrderAction.Ship ? "INSERT" : "UPDATE";
        SqlFixture.Execute(s.Target, $"CREATE TRIGGER dbo.FulfillmentFailure ON dbo.{table} AFTER {triggerEvent} AS THROW 51006, 'Isolated test failure', 1;");
        var actor = action == OrderAction.Ship ? s.Seller : s.Buyer;
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ActAsync(actor, action, s.Order));
        Assert.Equal(action == OrderAction.Ship ? "Pending" : "Shipped", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.OrderTable WHERE id={s.Order}"));
        Assert.Equal(action == OrderAction.Ship ? "0" : "1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        Assert.Equal("Pending", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.Payment WHERE orderId={s.Order}"));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Payment WHERE orderId={s.Order} AND paidAt IS NOT NULL"));
        if (action == OrderAction.Receive) Assert.Equal("Shipped", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.ShippingInfo WHERE orderId={s.Order}"));
        SqlFixture.Execute(s.Target, $"DISABLE TRIGGER dbo.FulfillmentFailure ON dbo.{table}");
        Assert.Equal(OrderOutcome.Success, (await service.ActAsync(actor, action, s.Order)).Outcome);
    }

    [Fact]
    public async Task History_pages_are_bounded_stable_filtered_and_legacy_seed_is_owned()
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderTable(buyerId,SellerId,status) SELECT TOP(24) {s.Buyer},{s.Seller},'Cancelled' FROM sys.objects");
        await using var db = Open(s.Target);
        var service = new OrderService(db);
        var first = await service.HistoryAsync(s.Buyer, OrderAudience.Buyer);
        var second = await service.HistoryAsync(s.Buyer, OrderAudience.Buyer, 2);
        Assert.Equal(25, first.Total);
        Assert.Equal(20, first.Orders.Count);
        Assert.Equal(5, second.Orders.Count);
        Assert.Empty(first.Orders.Select(x => x.Id).Intersect(second.Orders.Select(x => x.Id)));
        Assert.Equal(first.Orders.OrderByDescending(x => x.Id).ToList(), first.Orders);
        Assert.Empty((await service.HistoryAsync(s.Buyer, OrderAudience.Buyer, 3)).Orders);
        Assert.Single((await service.HistoryAsync(s.Seller, OrderAudience.Seller, status:"Pending")).Orders);
        var seedBuyer = await db.Set<User>().Where(x => x.Email == "buyer.a06@example.invalid").Select(x => x.Id).SingleAsync();
        var seedSeller = await db.Set<User>().Where(x => x.Email == "seller.a06@example.invalid").Select(x => x.Id).SingleAsync();
        Assert.Equal(4, (await service.HistoryAsync(seedBuyer, OrderAudience.Buyer)).Total);
        Assert.Equal(4, (await service.HistoryAsync(seedSeller, OrderAudience.Seller)).Total);
        await Assert.ThrowsAsync<ArgumentException>(() => service.HistoryAsync(s.Buyer, OrderAudience.Buyer, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.HistoryAsync(s.Buyer, OrderAudience.Buyer, status:"invalid"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.HistoryAsync(s.Seller, OrderAudience.Buyer));
    }

    [Fact]
    public void Snapshot_migration_preserves_B05_data_and_leaves_ambiguous_legacy_orders_unowned()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        var scripts = new List<SqlScript> { new("0001_Baseline.sql", Baseline.ReadTableDdl()) };
        var assembly = typeof(MigrationRunner).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".Scripts.") && !x.EndsWith("0005_OrderSellerSnapshot.sql") && !x.EndsWith("0006_ChatConversation.sql")
            && !x.EndsWith("0016_UserJoinedAt.sql")).Order()) // 0016 indexes the chat columns added by 0006.
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..], reader.ReadToEnd()));
        }
        Assert.True(MigrationRunner.BuildEngine(target,scripts).PerformUpgrade().Successful);
        DemoSeedRunner.Run(target);
        var before = SqlFixture.Rows(target, SqlFixture.Metadata);
        SqlFixture.Execute(target, "INSERT dbo.[User](email,role) VALUES(N'other@migration.example.invalid',N'Seller'); INSERT dbo.Product(title,sellerId) SELECT N'Other item',id FROM dbo.[User] WHERE email=N'other@migration.example.invalid'; INSERT dbo.OrderItem(orderId,productId) SELECT MIN(o.id),MAX(p.id) FROM dbo.OrderTable o CROSS JOIN dbo.Product p;");
        Assert.Equal(3, MigrationRunner.Run(target));
        Assert.True(before.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal("1", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE SellerId IS NULL"));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE SellerId IS NOT NULL"));
        Assert.Equal("9", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderItem"));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Payment"));
        Assert.Equal(0, MigrationRunner.Run(target));
        Assert.False(DemoSeedRunner.Run(target));
    }

    private static string Field(string html) => WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Login(HttpClient client, string who)
    {
        var page = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = who+"@b06.example.invalid", ["Password"] = "Orders-Test-Password!", ["__RequestVerificationToken"] = Field(page) }))).StatusCode);
    }

    [Fact]
    public async Task Http_orders_enforce_roles_objects_csrf_and_buyer_seller_journey()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var buyer = factory.CreateClient(new() { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });
        using var seller = factory.CreateClient(new() { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });
        using var stranger = factory.CreateClient(new() { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect,(await buyer.GetAsync("/Orders/Purchases")).StatusCode);
        await Login(buyer,"buyer"); await Login(seller,"seller"); await Login(stranger,"otherseller");
        var purchases = await buyer.GetStringAsync("/Orders/Purchases");
        Assert.Contains("Purchase history",purchases); Assert.Contains("Wireless Headphones",purchases);
        Assert.DoesNotContain("password",purchases,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden,(await buyer.GetAsync("/Orders/Sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await seller.GetAsync("/Orders/Purchases")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/Orders/Details/{s.Order}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await buyer.GetAsync("/Orders/Details/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await buyer.GetAsync("/Orders/Purchases?page=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await buyer.GetAsync("/Orders/Purchases?status=other")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await buyer.GetAsync("/Orders/Purchases?status=")).StatusCode);
        var sellerPage = await seller.GetStringAsync($"/Orders/Details/{s.Order}");
        Assert.Contains("Mark as shipped",sellerPage); Assert.DoesNotContain("Confirm received",sellerPage);
        Assert.Equal(HttpStatusCode.BadRequest,(await seller.PostAsync("/Orders/Ship",new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = s.Order.ToString() }))).StatusCode);
        var shipForm = new Dictionary<string,string> { ["id"] = s.Order.ToString(), ["__RequestVerificationToken"] = Field(sellerPage), ["SellerId"] = s.OtherSeller.ToString(), ["status"]="Delivered" };
        Assert.Equal(HttpStatusCode.Redirect,(await seller.PostAsync("/Orders/Ship",new FormUrlEncodedContent(shipForm))).StatusCode);
        var buyerPage = await buyer.GetStringAsync($"/Orders/Details/{s.Order}");
        Assert.Contains("Confirm received",buyerPage);
        var receiveForm = new Dictionary<string,string> { ["id"] = s.Order.ToString(), ["__RequestVerificationToken"] = Field(buyerPage), ["amount"]="0.01" };
        Assert.Equal(HttpStatusCode.Redirect,(await buyer.PostAsync("/Orders/Receive",new FormUrlEncodedContent(receiveForm))).StatusCode);
        Assert.Contains("Payment: COD",await buyer.GetStringAsync($"/Orders/Details/{s.Order}"));
        Assert.Equal("Delivered",SqlFixture.Scalar(s.Target,$"SELECT status FROM dbo.OrderTable WHERE id={s.Order}"));
        Assert.Equal("99.80",SqlFixture.Scalar(s.Target,$"SELECT amount FROM dbo.Payment WHERE orderId={s.Order}"));
    }
}
