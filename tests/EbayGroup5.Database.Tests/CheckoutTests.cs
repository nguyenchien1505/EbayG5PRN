using System.Net;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Checkout;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DbUp.Engine;

namespace EbayGroup5.Database.Tests;

public sealed class CheckoutTests
{
    private static EbayDbContext Open(string target) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
    private static async Task<(string Target, int Buyer, int Other, int Address, int OtherAddress, int Product)> Setup()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        await using var db = Open(target);
        var accounts = new AccountService(db);
        Assert.True(await accounts.RegisterAsync("Checkout Buyer", "checkout@example.invalid", "Checkout-Test-Password!"));
        Assert.True(await accounts.RegisterAsync("Other Buyer", "othercheckout@example.invalid", "Checkout-Test-Password!"));
        var buyer = await db.Set<User>().SingleAsync(x => x.Email == "CHECKOUT@EXAMPLE.INVALID");
        var other = await db.Set<User>().SingleAsync(x => x.Email == "OTHERCHECKOUT@EXAMPLE.INVALID");
        var service = new CheckoutService(db);
        var input = new AddressInput("Test Buyer", "0123456789", "Test street", "Test city", null, "Vietnam");
        var address = await service.AddAddressAsync(buyer.Id, input);
        var otherAddress = await service.AddAddressAsync(other.Id, input);
        var product = await db.Set<Product>().SingleAsync(x => x.Title == "Wireless Headphones");
        return (target, buyer.Id, other.Id, address!.Value, otherAddress!.Value, product.Id);
    }

    [Fact]
    public async Task Order_payment_price_stock_and_replay_are_atomic_and_owned()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var service = new CheckoutService(db);
        var request = new CheckoutRequest(s.Product, s.Address, 2, 49.90m, Guid.NewGuid());
        var result = await service.PlaceAsync(s.Buyer, request);
        Assert.Null(result.Error);
        var order = await service.FindAsync(s.Buyer, result.OrderId!.Value);
        Assert.Equal(99.80m, order!.Total);
        Assert.Equal("Pending", order.Status);
        Assert.Equal("COD", order.PaymentMethod);
        Assert.Equal("Pending", order.PaymentStatus);
        Assert.Null((await db.Set<Payment>().SingleAsync(x => x.OrderId == order.Id)).PaidAt);
        Assert.Equal(18, await db.Set<Inventory>().Where(x => x.ProductId == s.Product).SumAsync(x => x.Quantity));
        Assert.Null(await service.FindAsync(s.Other, order.Id));
        Assert.Null(await service.FindAsync(s.Buyer, int.MaxValue));
        await db.Set<Product>().Where(x => x.Id == s.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 99m));
        Assert.Equal(order.Id, (await service.PlaceAsync(s.Buyer, request)).OrderId);
        Assert.True((await service.PlaceAsync(s.Buyer, request with { Quantity = 1 })).Conflict);
        Assert.Equal(1, await db.Set<OrderTable>().CountAsync(x => x.BuyerId == s.Buyer));
        Assert.Equal(18, await db.Set<Inventory>().Where(x => x.ProductId == s.Product).SumAsync(x => x.Quantity));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("price")]
    [InlineData("quantity")]
    [InlineData("auction")]
    [InlineData("seller")]
    [InlineData("overflow")]
    [InlineData("key")]
    [InlineData("stock")]
    [InlineData("missing")]
    [InlineData("address")]
    public async Task Invalid_requests_leave_no_order_or_stock_change(string invalid)
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var request = new CheckoutRequest(s.Product, s.Address, 1, 49.90m, Guid.NewGuid());
        var buyer = s.Buyer;
        if (invalid == "foreign") request = request with { AddressId = s.OtherAddress };
        if (invalid == "price") request = request with { ExpectedUnitPrice = 0.01m };
        if (invalid == "quantity") request = request with { Quantity = 0 };
        if (invalid == "key") request = request with { Key = Guid.Empty };
        if (invalid == "stock") request = request with { Quantity = 21 };
        if (invalid == "missing") request = request with { ProductId = int.MaxValue };
        if (invalid == "address") request = request with { AddressId = int.MaxValue };
        if (invalid == "auction") await db.Set<Product>().Where(x => x.Id == s.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsAuction, true));
        if (invalid == "seller") buyer = await db.Set<User>().Where(x => x.Role == "Seller").Select(x => x.Id).SingleAsync();
        if (invalid == "overflow")
        {
            await db.Set<Product>().Where(x => x.Id == s.Product).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 99999999.99m));
            request = request with { ExpectedUnitPrice = 99999999.99m, Quantity = 2 };
        }
        Assert.NotNull((await new CheckoutService(db).PlaceAsync(buyer, request)).Error);
        Assert.Equal(0, await db.Set<OrderTable>().CountAsync(x => x.CheckoutKey != null));
        Assert.Equal(20, await db.Set<Inventory>().Where(x => x.ProductId == s.Product).SumAsync(x => x.Quantity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_buyers_and_duplicate_attempts_cannot_oversell(bool duplicate)
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Inventory SET quantity=1 WHERE productId={s.Product}");
        var request = new CheckoutRequest(s.Product, s.Address, 1, 49.90m, Guid.NewGuid());
        await using var first = Open(s.Target);
        await using var second = Open(s.Target);
        var results = await Task.WhenAll(new CheckoutService(first).PlaceAsync(s.Buyer, request),
            new CheckoutService(second).PlaceAsync(duplicate ? s.Buyer : s.Other, duplicate ? request : request with { AddressId = s.OtherAddress }));
        Assert.Equal(duplicate ? 2 : 1, results.Count(x => x.OrderId != null));
        if (duplicate) Assert.Equal(results[0].OrderId, results[1].OrderId);
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE CheckoutKey IS NOT NULL"));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, $"SELECT SUM(quantity) FROM dbo.Inventory WHERE productId={s.Product}"));
    }

    [Fact]
    public async Task Multiple_inventory_rows_and_same_key_for_distinct_buyers_work()
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Inventory SET quantity=1 WHERE productId={s.Product}; INSERT dbo.Inventory(productId,quantity) VALUES({s.Product},2),({s.Product},NULL),({s.Product},-3)");
        await using var db = Open(s.Target);
        var service = new CheckoutService(db);
        var key = Guid.NewGuid();
        Assert.NotNull((await service.PlaceAsync(s.Buyer, new(s.Product, s.Address, 2, 49.90m, key))).OrderId);
        Assert.NotNull((await service.PlaceAsync(s.Other, new(s.Product, s.OtherAddress, 1, 49.90m, key))).OrderId);
        Assert.NotNull((await service.PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()))).Error);
        Assert.Equal(0, await db.Set<Inventory>().CountAsync(x => x.ProductId == s.Product && x.Quantity > 0));
        Assert.Equal(1, await db.Set<Inventory>().CountAsync(x => x.ProductId == s.Product && x.Quantity == -3));
    }

    [Fact]
    public async Task Failure_after_order_insert_rolls_back_all_writes_and_allows_retry()
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, "CREATE TRIGGER dbo.TestPaymentFailure ON dbo.Payment AFTER INSERT AS THROW 51005, 'Test failure', 1;");
        await using var db = Open(s.Target);
        var service = new CheckoutService(db);
        var request = new CheckoutRequest(s.Product, s.Address, 1, 49.90m, Guid.NewGuid());
        await Assert.ThrowsAsync<DbUpdateException>(() => service.PlaceAsync(s.Buyer, request));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE CheckoutKey IS NOT NULL"));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Payment WHERE userId={s.Buyer}"));
        Assert.Equal("20", SqlFixture.Scalar(s.Target, $"SELECT SUM(quantity) FROM dbo.Inventory WHERE productId={s.Product}"));
        Assert.Equal("8", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderItem"));
        SqlFixture.Execute(s.Target, "DISABLE TRIGGER dbo.TestPaymentFailure ON dbo.Payment");
        Assert.NotNull((await service.PlaceAsync(s.Buyer, request)).OrderId);
    }

    [Fact]
    public async Task Upgrade_keeps_legacy_orders_null_and_unique_key_is_enforced()
    {
        var s = await Setup();
        Assert.Equal("4", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE CheckoutKey IS NULL AND CheckoutFingerprint IS NULL"));
        Assert.Equal(0, MigrationRunner.Run(s.Target));
        await using var db = Open(s.Target);
        var request = new CheckoutRequest(s.Product, s.Address, 1, 49.90m, Guid.NewGuid());
        await new CheckoutService(db).PlaceAsync(s.Buyer, request);
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"INSERT dbo.OrderTable(buyerId,CheckoutKey) VALUES({s.Buyer},'{request.Key}')"));
    }

    [Fact]
    public void Migration_upgrades_populated_B04_database_preserving_seed_and_metadata()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        var scripts = new List<SqlScript> { new("0001_Baseline.sql", Baseline.ReadTableDdl()) };
        var assembly = typeof(MigrationRunner).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.EndsWith("0002_AppRole.sql") || x.EndsWith("0003_DemoSeedHistory.sql")).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..], reader.ReadToEnd()));
        }
        Assert.True(MigrationRunner.BuildEngine(target, scripts).PerformUpgrade().Successful);
        DemoSeedRunner.Run(target);
        var before = SqlFixture.Rows(target, SqlFixture.Metadata);
        Assert.Equal(11, MigrationRunner.Run(target));
        Assert.True(before.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE CheckoutKey IS NULL AND CheckoutFingerprint IS NULL"));
        Assert.Equal("8", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderItem"));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Payment"));
        Assert.Equal("20", SqlFixture.Scalar(target, "SELECT quantity FROM dbo.Inventory WHERE productId=(SELECT id FROM dbo.Product WHERE title=N'Wireless Headphones')"));
        Assert.False(DemoSeedRunner.Run(target));
    }

    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static async Task Login(HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = "checkout@example.invalid", ["Password"] = "Checkout-Test-Password!", ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken") }))).StatusCode);
    }

    [Fact]
    public async Task Http_checkout_enforces_principal_csrf_address_ownership_and_replay()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/Checkout?productId={s.Product}")).StatusCode);
        await Login(client);
        var html = await client.GetStringAsync($"/Checkout?productId={s.Product}");
        Assert.Contains("Place order", html);
        Assert.DoesNotContain($"value=\"{s.OtherAddress}\"", html);
        var fields = new Dictionary<string,string> { ["ProductId"] = s.Product.ToString(), ["AddressId"] = s.OtherAddress.ToString(), ["Quantity"] = "2",
            ["ExpectedUnitPrice"] = "49.90", ["Key"] = Field(html,"Key"), ["BuyerId"] = s.Other.ToString(), ["TotalPrice"] = "0.01", ["Method"] = "Paid" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Checkout/Place", new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"] = Field(html,"__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Checkout/Place", new FormUrlEncodedContent(fields))).StatusCode);
        fields["AddressId"] = s.Address.ToString();
        var response = await client.PostAsync("/Checkout/Place", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var replay = await client.PostAsync("/Checkout/Place", new FormUrlEncodedContent(fields));
        Assert.Equal(response.Headers.Location, replay.Headers.Location);
        Assert.Contains("Your order is placed", await client.GetStringAsync(response.Headers.Location));
        Assert.Equal("99.80", SqlFixture.Scalar(s.Target, $"SELECT totalPrice FROM dbo.OrderTable WHERE buyerId={s.Buyer}"));
        Assert.Equal("COD", SqlFixture.Scalar(s.Target, $"SELECT method FROM dbo.Payment WHERE userId={s.Buyer}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Checkout/Confirmation/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Checkout?productId={s.Product}&quantity=0")).StatusCode);
        using var other = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var loginPage = await other.GetStringAsync("/Account/Login");
        await other.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = "othercheckout@example.invalid", ["Password"] = "Checkout-Test-Password!", ["__RequestVerificationToken"] = Field(loginPage,"__RequestVerificationToken") }));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(response.Headers.Location)).StatusCode);
        await using var db = Open(s.Target);
        await db.Set<User>().Where(x => x.Id == s.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Seller"));
        await Login(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Checkout?productId={s.Product}")).StatusCode);
    }

    [Fact]
    public async Task Http_new_address_uses_principal_validates_fields_and_selects_saved_address()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await Login(client);
        var html = await client.GetStringAsync($"/Checkout/AddAddress?productId={s.Product}&quantity=2");
        var form = new Dictionary<string,string> { ["ProductId"] = s.Product.ToString(), ["Quantity"] = "2", ["FullName"] = "New Recipient",
            ["Phone"] = "0123456789", ["Street"] = "New street", ["City"] = "New city", ["Country"] = "Vietnam", ["UserId"] = s.Other.ToString() };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Checkout/AddAddress", new FormUrlEncodedContent(form))).StatusCode);
        form["__RequestVerificationToken"] = Field(html,"__RequestVerificationToken");
        form["FullName"] = new string('x', 101);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Checkout/AddAddress", new FormUrlEncodedContent(form))).StatusCode);
        form["FullName"] = "New Recipient";
        var response = await client.PostAsync("/Checkout/AddAddress", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("quantity=2", response.Headers.Location!.ToString());
        await using var db = Open(s.Target);
        var address = await db.Set<Address>().SingleAsync(x => x.FullName == "New Recipient");
        Assert.Equal(s.Buyer, address.UserId);
        var checkoutPage = await client.GetStringAsync(response.Headers.Location);
        Assert.Matches($"name=\"AddressId\" value=\"{address.Id}\" checked=\"checked\"", checkoutPage);
    }
}
