using System.Net;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Orders;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Orders;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Infrastructure.Reviews;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ReviewTests
{
    internal const string Password = "Reviews-Test-Password!";
    internal static EbayDbContext Open(string target) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
    internal sealed record SetupData(string Target, int Buyer, int Seller, int OtherBuyer, int OtherSeller, int Admin, int Address, int Order, int Item, int Product, int OtherProduct);

    internal static async Task<SetupData> Setup()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        await using var db = Open(target);
        var accounts = new AccountService(db);
        foreach (var name in new[] { "buyer", "seller", "otherbuyer", "otherseller" })
            Assert.True(await accounts.RegisterAsync(name, name + "@d01.example.invalid", Password));
        var users = await db.Set<User>().Where(x => x.Email!.EndsWith("@D01.EXAMPLE.INVALID")).ToDictionaryAsync(x => x.Username!);
        foreach (var name in new[] { "seller", "otherseller" }) users[name].Role = "Seller";
        var product = await db.Set<Product>().SingleAsync(x => x.Title == "Wireless Headphones");
        var other = await db.Set<Product>().SingleAsync(x => x.Title == "Desk Lamp");
        product.SellerId = users["seller"].Id;
        await db.SaveChangesAsync();
        var checkout = new CheckoutService(db);
        var address = await checkout.AddAddressAsync(users["buyer"].Id, new("Test Buyer", "0123456789", "Example street", "Example city", null, "Vietnam"));
        var placed = await checkout.PlaceAsync(users["buyer"].Id, new(product.Id, address!.Value, 1, 49.90m, Guid.NewGuid()));
        var admin = await db.Set<User>().Where(x => x.Role == "Admin").Select(x => x.Id).FirstAsync();
        var item = int.Parse(SqlFixture.Scalar(target, $"SELECT id FROM dbo.OrderItem WHERE orderId={placed.OrderId}"));
        return new(target, users["buyer"].Id, users["seller"].Id, users["otherbuyer"].Id, users["otherseller"].Id, admin, address.Value, placed.OrderId!.Value, item, product.Id, other.Id);
    }

    internal static async Task Deliver(SetupData s, int order)
    {
        await using var db = Open(s.Target);
        var orders = new OrderService(db);
        Assert.Equal(OrderOutcome.Success, (await orders.ActAsync(s.Seller, OrderAction.Ship, order)).Outcome);
        Assert.Equal(OrderOutcome.Success, (await orders.ActAsync(s.Buyer, OrderAction.Receive, order)).Outcome);
    }

    private static string Reviews(SetupData s) => SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.Review");

    [Fact]
    public async Task Buyer_reviews_only_after_delivery_exactly_once_and_server_sets_identity()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        var before = Assert.Single((await service.ListForOrderAsync(s.Buyer, s.Order))!);
        Assert.False(before.CanReview);
        Assert.Contains("after the order is delivered", before.Reason);
        Assert.Equal("Wireless Headphones", before.Title);
        Assert.Equal(ReviewOutcome.Conflict, (await service.SubmitAsync(s.Buyer, s.Item, 5, "too early")).Outcome);
        await using (var fulfillment = Open(s.Target))
            Assert.Equal(OrderOutcome.Success, (await new OrderService(fulfillment).ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
        Assert.Equal(ReviewOutcome.Conflict, (await service.SubmitAsync(s.Buyer, s.Item, 5, "still shipping")).Outcome);
        Assert.Equal("0", Reviews(s));

        await using (var fulfillment = Open(s.Target))
            Assert.Equal(OrderOutcome.Success, (await new OrderService(fulfillment).ActAsync(s.Buyer, OrderAction.Receive, s.Order)).Outcome);
        Assert.True(Assert.Single((await service.ListForOrderAsync(s.Buyer, s.Order))!).CanReview);
        var result = await service.SubmitAsync(s.Buyer, s.Item, 5, "  Great item\n ");
        Assert.Equal(ReviewOutcome.Success, result.Outcome);
        Assert.Equal(s.Order, result.OrderId);
        Assert.Equal($"{s.Product}|{s.Buyer}|{s.Item}|5|Great item", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(productId,'|',reviewerId,'|',OrderItemId,'|',rating,'|',comment) FROM dbo.Review WHERE id={result.ReviewId}"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Review WHERE createdAt>=DATEADD(minute,-5,SYSUTCDATETIME()) AND createdAt<=DATEADD(minute,5,SYSUTCDATETIME())"));

        var second = await service.SubmitAsync(s.Buyer, s.Item, 1, "changed my mind");
        Assert.Equal(ReviewOutcome.Conflict, second.Outcome);
        Assert.Equal("1", Reviews(s));
        Assert.Equal("5|Great item", SqlFixture.Scalar(s.Target, "SELECT CONCAT(rating,'|',comment) FROM dbo.Review"));
        var after = Assert.Single((await service.ListForOrderAsync(s.Buyer, s.Order))!);
        Assert.True(after.Reviewed);
        Assert.False(after.CanReview);
        Assert.Equal(result.ReviewId, after.ReviewId);
    }

    [Fact]
    public async Task Wrong_owner_role_and_missing_items_are_rejected_without_rows()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        Assert.Equal(ReviewOutcome.NotFound, (await service.SubmitAsync(s.OtherBuyer, s.Item, 5, "foreign")).Outcome);
        Assert.Equal(ReviewOutcome.Forbidden, (await service.SubmitAsync(s.Seller, s.Item, 5, "seller")).Outcome);
        Assert.Equal(ReviewOutcome.Forbidden, (await service.SubmitAsync(s.OtherSeller, s.Item, 5, "other seller")).Outcome);
        Assert.Equal(ReviewOutcome.Forbidden, (await service.SubmitAsync(s.Admin, s.Item, 5, "admin")).Outcome);
        Assert.Equal(ReviewOutcome.Forbidden, (await service.SubmitAsync(int.MaxValue, s.Item, 5, "unknown actor")).Outcome);
        Assert.Equal(ReviewOutcome.NotFound, (await service.SubmitAsync(s.Buyer, int.MaxValue, 5, "missing item")).Outcome);
        Assert.Equal(ReviewOutcome.NotFound, (await service.SubmitAsync(s.Buyer, 0, 5, "zero item")).Outcome);
        Assert.Null(await service.ListForOrderAsync(s.OtherBuyer, s.Order));
        Assert.Null(await service.ListForOrderAsync(s.Seller, s.Order));
        Assert.Null(await service.ListForOrderAsync(s.Buyer, int.MaxValue));
        Assert.Equal("0", Reviews(s));
    }

    [Theory]
    [InlineData("ratingZero")]
    [InlineData("ratingSix")]
    [InlineData("ratingNegative")]
    [InlineData("ratingMin")]
    [InlineData("ratingMax")]
    [InlineData("longComment")]
    [InlineData("nulCharacter")]
    [InlineData("bellCharacter")]
    public async Task Invalid_rating_or_comment_is_rejected_before_any_write(string kind)
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var (rating, comment) = kind switch
        {
            "ratingZero" => (0, "ok"),
            "ratingSix" => (6, "ok"),
            "ratingNegative" => (-1, "ok"),
            "ratingMin" => (int.MinValue, "ok"),
            "ratingMax" => (int.MaxValue, "ok"),
            "longComment" => (5, new string('x', ReviewLimits.MaxCommentLength + 1)),
            "nulCharacter" => (5, "bad\0text"),
            _ => (5, "bell\a")
        };
        await using var db = Open(s.Target);
        var result = await new ReviewService(db).SubmitAsync(s.Buyer, s.Item, rating, comment);
        Assert.Equal(ReviewOutcome.Invalid, result.Outcome);
        Assert.Equal("0", Reviews(s));
        Assert.True(Assert.Single((await new ReviewService(db).ListForOrderAsync(s.Buyer, s.Order))!).CanReview);
    }

    [Fact]
    public async Task Boundary_ratings_and_comments_are_normalized_and_stored()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) SELECT orderId,productId,quantity,unitPrice FROM dbo.OrderItem WHERE orderId={s.Order}; " +
            $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) SELECT orderId,productId,quantity,unitPrice FROM dbo.OrderItem WHERE orderId={s.Order} AND id={s.Item};");
        var ids = SqlFixture.Rows(s.Target, $"SELECT CONVERT(varchar(12),id) FROM dbo.OrderItem WHERE orderId={s.Order}").Select(int.Parse).Order().ToArray();
        Assert.Equal(3, ids.Length);
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        var emoji = string.Concat(Enumerable.Repeat("😀", ReviewLimits.MaxCommentLength / 2));
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, ids[0], 1, emoji)).Outcome);
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, ids[1], 5, " \t\r\n ")).Outcome);
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, ids[2], 3, "\t keep\r\ninner\tline \n")).Outcome);
        Assert.Equal("3", Reviews(s));
        await using var read = Open(s.Target);
        var stored = await read.Set<Review>().AsNoTracking().OrderBy(x => x.OrderItemId).ToListAsync();
        Assert.Equal(emoji, stored[0].Comment);
        Assert.Equal(new[] { 1, 5, 3 }, stored.Select(x => x.Rating!.Value).ToArray());
        Assert.Null(stored[1].Comment);
        Assert.Equal("keep\r\ninner\tline", stored[2].Comment);
        Assert.Equal(3, (await service.ListForOrderAsync(s.Buyer, s.Order))!.Count(x => x.Reviewed));
    }

    [Fact]
    public async Task Repurchasing_the_same_product_in_another_order_can_be_reviewed_again()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        await using var db = Open(s.Target);
        var again = await new CheckoutService(db).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()));
        var secondItem = int.Parse(SqlFixture.Scalar(s.Target, $"SELECT id FROM dbo.OrderItem WHERE orderId={again.OrderId}"));
        var service = new ReviewService(db);
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, s.Item, 4, "first purchase")).Outcome);
        Assert.Equal(ReviewOutcome.Conflict, (await service.SubmitAsync(s.Buyer, secondItem, 5, "second purchase, not delivered")).Outcome);
        await Deliver(s, again.OrderId!.Value);
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, secondItem, 5, "second purchase")).Outcome);
        Assert.Equal("2", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Review WHERE productId={s.Product} AND reviewerId={s.Buyer}"));
        Assert.Equal("2", SqlFixture.Scalar(s.Target, "SELECT COUNT(DISTINCT OrderItemId) FROM dbo.Review"));
    }

    [Fact]
    public async Task Concurrent_submissions_create_exactly_one_review_per_item()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        SqlFixture.Execute(s.Target, string.Concat(Enumerable.Repeat($"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90);", 3)));
        var ids = SqlFixture.Rows(s.Target, $"SELECT CONVERT(varchar(12),id) FROM dbo.OrderItem WHERE orderId={s.Order}").Select(int.Parse).ToArray();
        foreach (var id in ids)
        {
            await using var first = Open(s.Target);
            await using var second = Open(s.Target);
            var results = await Task.WhenAll(new ReviewService(first).SubmitAsync(s.Buyer, id, 5, "first"), new ReviewService(second).SubmitAsync(s.Buyer, id, 1, "second"));
            Assert.Single(results, x => x.Outcome == ReviewOutcome.Success);
            Assert.Single(results, x => x.Outcome == ReviewOutcome.Conflict);
            Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Review WHERE OrderItemId={id}"));
        }
        Assert.Equal(ids.Length.ToString(), Reviews(s));
    }

    [Fact]
    public async Task Failure_while_writing_rolls_back_and_retry_succeeds()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        SqlFixture.Execute(s.Target, "CREATE TRIGGER dbo.ReviewFailure ON dbo.Review AFTER INSERT AS THROW 51007, 'Isolated test failure', 1;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SubmitAsync(s.Buyer, s.Item, 5, "will fail"));
        Assert.Equal("0", Reviews(s));
        SqlFixture.Execute(s.Target, "DISABLE TRIGGER dbo.ReviewFailure ON dbo.Review");
        Assert.Equal(ReviewOutcome.Success, (await service.SubmitAsync(s.Buyer, s.Item, 5, "retry")).Outcome);
        Assert.Equal("1", Reviews(s));
    }

    [Fact]
    public async Task Database_constraints_reject_inconsistent_links_but_keep_legacy_rows_valid()
    {
        var s = await Setup();
        string Insert(string columns, string values) => $"INSERT dbo.Review({columns}) VALUES({values})";
        const string cols = "productId,reviewerId,OrderItemId,rating,comment,createdAt";
        var now = "SYSUTCDATETIME()";
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.OtherProduct},{s.Buyer},{s.Item},5,N'wrong product',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},2147483647,5,N'missing item',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},9,N'rating high',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},0,N'rating low',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},NULL,N'no rating',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},NULL,{s.Item},5,N'no reviewer',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},5,N'no date',NULL")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"NULL,{s.Buyer},{s.Item},5,N'no product',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},5,N'   ',{now}")));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},5,REPLICATE(CAST(N'x' AS nvarchar(max)),2001),{now}")));
        Assert.Equal("0", Reviews(s));

        SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},5,NULL,{now}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(cols, $"{s.Product},{s.Buyer},{s.Item},4,N'duplicate',{now}")));
        Assert.Equal("1", Reviews(s));

        // Legacy-shaped rows (no OrderItemId) remain legal and may repeat; D01 never rewrites them.
        SqlFixture.Execute(s.Target, "INSERT dbo.Review(productId,reviewerId,rating,comment) VALUES(" + s.Product + "," + s.Buyer + ",9,N'legacy');" +
            "INSERT dbo.Review(productId,reviewerId,rating,comment) VALUES(" + s.Product + "," + s.Buyer + ",9,N'legacy');INSERT dbo.Review(rating) VALUES(NULL)");
        Assert.Equal("4", Reviews(s));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'UX_Review_OrderItem' AND is_unique=1 AND has_filter=1"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.key_constraints WHERE name=N'UQ_OrderItem_Id_Product' AND type='UQ'"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name=N'FK_Review_OrderItemProduct' AND is_not_trusted=0 AND is_disabled=0"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_Review_OrderLink' AND is_not_trusted=0 AND is_disabled=0"));
    }

    [Fact]
    public void Migration_adds_the_link_to_populated_database_without_touching_legacy_reviews()
    {
        var target = SqlFixture.NewTarget();
        var remaining = SqlFixture.MigrateBefore(target, "0007");
        DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.Review(productId,reviewerId,rating,comment,createdAt) SELECT TOP(1) id,sellerId,9,N'legacy out of range','20260901' FROM dbo.Product; " +
            "INSERT dbo.Review(productId,reviewerId,rating,comment,createdAt) SELECT TOP(1) id,sellerId,3,NULL,NULL FROM dbo.Product; INSERT dbo.Review(comment) VALUES(N'orphan legacy')");
        var metadata = SqlFixture.Rows(target, SqlFixture.Metadata);
        const string reviewRows = "SELECT CONCAT(id,'|',productId,'|',reviewerId,'|',rating,'|',comment,'|',CONVERT(nvarchar(30),createdAt,126)) FROM dbo.Review";
        var legacy = SqlFixture.Rows(target, reviewRows);
        Assert.Equal(3, legacy.Count);
        Assert.Equal(remaining, MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.True(legacy.SetEquals(SqlFixture.Rows(target, reviewRows)));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Review WHERE OrderItemId IS NULL"));
        Assert.Equal("8", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderItem"));
        Assert.Equal(0, MigrationRunner.Run(target));
        Assert.False(DemoSeedRunner.Run(target));
        SqlFixture.Execute(target, "INSERT dbo.Review(rating) VALUES(11)");
    }

    internal static string Field(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    internal static async Task<HttpClient> Login(AccountHttpTests.Factory factory, string who)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var page = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = who + "@d01.example.invalid", ["Password"] = Password, ["__RequestVerificationToken"] = Field(page) }))).StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, Dictionary<string, string> form, bool token = true)
    {
        if (token) form["__RequestVerificationToken"] = Field(await client.GetStringAsync("/Account/Me"));
        return await client.PostAsync("/Reviews/Create", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task Http_endpoint_enforces_csrf_roles_owner_and_ignores_overposted_identity()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var stranger = await Login(factory, "otherbuyer");
        using var seller = await Login(factory, "seller");
        var form = () => new Dictionary<string, string> { ["OrderItemId"] = s.Item.ToString(), ["Rating"] = "4", ["Comment"] = "HTTP review" };

        Assert.Equal(HttpStatusCode.Redirect, (await Post(anonymous, form(), token: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, form(), token: false)).StatusCode);
        foreach (var bad in new[] { ("Rating", "0"), ("Rating", "6"), ("Rating", "x"), ("Rating", ""), ("OrderItemId", "0"), ("OrderItemId", "-4"), ("OrderItemId", "abc") })
        {
            var invalid = form(); invalid[bad.Item1] = bad.Item2;
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, invalid)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, new() { ["OrderItemId"] = s.Item.ToString() })).StatusCode);
        Assert.Equal((HttpStatusCode)409, (await Post(buyer, form())).StatusCode); // Not delivered yet.
        Assert.Equal("0", Reviews(s));

        await Deliver(s, s.Order);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, form())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(seller, form())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(buyer, new() { ["OrderItemId"] = "2147483647", ["Rating"] = "5" })).StatusCode);
        Assert.Equal("0", Reviews(s));

        var over = form();
        over["ReviewerId"] = s.OtherBuyer.ToString(); over["reviewerId"] = s.OtherBuyer.ToString(); over["ProductId"] = s.OtherProduct.ToString();
        over["CreatedAt"] = "2001-01-01"; over["Id"] = "99"; over["OrderId"] = "99";
        var ok = await Post(buyer, over);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal($"/Orders/Details/{s.Order}", ok.Headers.Location!.OriginalString);
        Assert.True(ok.Headers.CacheControl?.NoStore == true);
        Assert.Equal($"{s.Product}|{s.Buyer}|{s.Item}|4|HTTP review", SqlFixture.Scalar(s.Target, "SELECT CONCAT(productId,'|',reviewerId,'|',OrderItemId,'|',rating,'|',comment) FROM dbo.Review"));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.Review WHERE createdAt<'20260101' OR id=99"));
        Assert.Equal((HttpStatusCode)409, (await Post(buyer, form())).StatusCode);
        Assert.Equal("1", Reviews(s));
    }
}
