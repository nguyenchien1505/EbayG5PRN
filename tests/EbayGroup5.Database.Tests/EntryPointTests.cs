using System.Net;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Orders;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Orders;
using EbayGroup5.Infrastructure.Reputation;
using EbayGroup5.Infrastructure.Reviews;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>D06: group 5 entry points (ENTRY_POINTS.md A1–A5).</summary>
public sealed class EntryPointTests
{
    private static async Task<int> Place(SetupData s)
    {
        await using var db = Open(s.Target);
        var placed = await new CheckoutService(db).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()));
        return placed.OrderId!.Value;
    }
    private static int FirstLine(SetupData s, int order) => int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MIN(id) FROM dbo.OrderItem WHERE orderId={order}"));
    private static void AddReview(SetupData s, int line, bool hidden = false) => SqlFixture.Execute(s.Target,
        $"INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt{(hidden ? ",HiddenAtUtc" : "")}) VALUES({s.Product},{s.Buyer},{line},4,NULL,SYSUTCDATETIME(){(hidden ? ",SYSUTCDATETIME()" : "")})");
    private static void AddFeedback(SetupData s, int line, int? seller = null, string sentiment = "Positive", bool hidden = false) => SqlFixture.Execute(s.Target,
        $"INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,CreatedAtUtc{(hidden ? ",HiddenAtUtc" : "")}) VALUES({line},{s.Buyer},{seller ?? s.Seller},'{sentiment}',SYSUTCDATETIME(){(hidden ? ",SYSUTCDATETIME()" : "")})");
    private static void PaidDaysAgo(SetupData s, int order, int days) =>
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Payment SET paidAt=DATEADD(day,-{days},SYSUTCDATETIME()) WHERE orderId={order}");

    [Fact]
    public async Task Ready_for_feedback_matches_the_order_details_button_for_every_order()
    {
        var s = await Setup();
        await Deliver(s, s.Order); // Nothing left yet: due on both parts.
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90)");
        var allDone = await Place(s); await Deliver(s, allDone); AddReview(s, FirstLine(s, allDone)); AddFeedback(s, FirstLine(s, allDone));
        var hiddenDone = await Place(s); await Deliver(s, hiddenDone); AddReview(s, FirstLine(s, hiddenDone), hidden: true); AddFeedback(s, FirstLine(s, hiddenDone), hidden: true);
        var expiredReviewed = await Place(s); await Deliver(s, expiredReviewed); PaidDaysAgo(s, expiredReviewed, 70); AddReview(s, FirstLine(s, expiredReviewed));
        var expiredOpenReview = await Place(s); await Deliver(s, expiredOpenReview); PaidDaysAgo(s, expiredOpenReview, 70);
        var edge = await Place(s); await Deliver(s, edge); PaidDaysAgo(s, edge, 59); AddReview(s, FirstLine(s, edge));
        var feedbackOnly = await Place(s); await Deliver(s, feedbackOnly); AddFeedback(s, FirstLine(s, feedbackOnly));
        var pending = await Place(s);
        var shipped = await Place(s);
        await using (var db = Open(s.Target)) Assert.Equal(OrderOutcome.Success, (await new OrderService(db).ActAsync(s.Seller, OrderAction.Ship, shipped)).Outcome);

        await using var read = Open(s.Target);
        var orders = new OrderService(read); var reviews = new ReviewService(read); var feedback = new FeedbackService(read, TimeProvider.System);
        var history = await orders.HistoryAsync(s.Buyer, OrderAudience.Buyer);
        Assert.Equal(9, history.Total);
        var expectedDue = new List<int>();
        foreach (var order in history.Orders)
        {
            var reviewLines = (await reviews.ListForOrderAsync(s.Buyer, order.Id))!.ToDictionary(x => x.OrderItemId);
            var feedbackLines = (await feedback.ListForOrderAsync(s.Buyer, order.Id))!;
            // Same condition as the Leave feedback button in Views/Orders/Details.cshtml.
            var open = feedbackLines.Where(x => x.CanLeave || reviewLines[x.OrderItemId].CanReview).Select(x => x.OrderItemId).Order().ToList();
            Assert.Equal(open.Count, order.FeedbackLines);
            Assert.Equal(open.Count == 0 ? null : open[0], order.FeedbackItemId);
            Assert.Equal(FirstLine(s, order.Id), order.ContactItemId);
            if (open.Count > 0) expectedDue.Add(order.Id);
        }
        Assert.Equal(new[] { s.Order, expiredOpenReview, edge, feedbackOnly }.Order(), expectedDue.Order());
        Assert.Equal(2, history.Orders.Single(x => x.Id == s.Order).FeedbackLines);

        var ready = await orders.HistoryAsync(s.Buyer, OrderAudience.Buyer, status: OrderHistory.ReadyForFeedback);
        Assert.Equal(expectedDue.Order(), ready.Orders.Select(x => x.Id).Order());
        Assert.Equal(4, ready.Total);
        Assert.Equal(OrderHistory.ReadyForFeedback, ready.Status);

        var sales = await orders.HistoryAsync(s.Seller, OrderAudience.Seller);
        Assert.Equal(9, sales.Total);
        Assert.All(sales.Orders, x => { Assert.Null(x.FeedbackItemId); Assert.Equal(0, x.FeedbackLines); Assert.Null(x.ContactItemId); });
        await Assert.ThrowsAsync<ArgumentException>(() => orders.HistoryAsync(s.Seller, OrderAudience.Seller, status: OrderHistory.ReadyForFeedback));
        await Assert.ThrowsAsync<ArgumentException>(() => orders.HistoryAsync(s.Buyer, OrderAudience.Buyer, status: "readyforfeedback"));
        Assert.Empty((await orders.HistoryAsync(s.OtherBuyer, OrderAudience.Buyer, status: OrderHistory.ReadyForFeedback)).Orders);
    }

    [Fact]
    public async Task Card_batches_match_the_product_page_and_seller_badge()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        string Line() => SqlFixture.Scalar(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90); SELECT SCOPE_IDENTITY();");
        foreach (var (rating, hidden, comment) in new[] { (5, false, "'Great'"), (4, false, "NULL"), (2, false, "'Meh'"), (1, true, "'Hidden'"), (5, false, "NULL") })
            SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt,HiddenAtUtc) VALUES({s.Product},{s.Buyer},{Line()},{rating},{comment},SYSUTCDATETIME(),{(hidden ? "SYSUTCDATETIME()" : "NULL")})");
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,rating,comment,createdAt) VALUES({s.Product},{s.Buyer},1,N'legacy',SYSUTCDATETIME())");
        foreach (var (sentiment, hidden, seller) in new[] { ("Positive", false, s.Seller), ("Positive", false, s.Seller), ("Negative", false, s.Seller), ("Negative", true, s.Seller), ("Neutral", false, s.OtherSeller) })
            AddFeedback(s, int.Parse(Line()), seller, sentiment, hidden);

        await using var db = Open(s.Target);
        var reviews = new ReviewService(db); var feedback = new FeedbackService(db, TimeProvider.System);
        var summaries = await reviews.SummariesAsync([s.Product, s.OtherProduct, s.Product, int.MaxValue]);
        Assert.Equal([s.Product], summaries.Keys);
        Assert.Equal((await reviews.ProductReviewsAsync(s.Product)).Summary.Ratings, summaries[s.Product].Ratings);
        Assert.Equal((await reviews.ProductReviewsAsync(s.Product)).Summary.Average, summaries[s.Product].Average);
        Assert.Equal((await reviews.ProductReviewsAsync(s.Product)).Summary.Distribution, summaries[s.Product].Distribution);
        Assert.Equal(4, summaries[s.Product].Ratings); Assert.Equal(4.0m, summaries[s.Product].Average); Assert.Equal(2, summaries[s.Product].Reviews);

        var reputations = await feedback.ReputationsAsync([s.Seller, s.OtherSeller, s.Buyer]);
        foreach (var id in new[] { s.Seller, s.OtherSeller, s.Buyer }) Assert.Equal(await feedback.ReputationAsync(id), reputations[id]);
        // H01: one buyer, one transaction week -> counted once per sentiment (raw 2 P / 1 N).
        Assert.Equal(0, reputations[s.Seller].Score); Assert.Equal(50m, reputations[s.Seller].PositivePercent);
        Assert.Null(reputations[s.OtherSeller].PositivePercent); Assert.Equal(SellerReputation.Empty, reputations[s.Buyer]);

        Assert.Empty(await reviews.SummariesAsync([]));
        Assert.Empty(await feedback.ReputationsAsync([]));
        var tooMany = Enumerable.Range(1, 101).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.SummariesAsync(tooMany));
        await Assert.ThrowsAsync<ArgumentException>(() => feedback.ReputationsAsync(tooMany));
        Assert.Single(await reviews.SummariesAsync(Enumerable.Repeat(s.Product, 150).ToArray())); // Limit applies to distinct ids.
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1K")]
    [InlineData(1299, "1.2K")]
    [InlineData(12345, "12.3K")]
    [InlineData(999999, "999.9K")]
    [InlineData(1500000, "1.5M")]
    [InlineData(-14, "-14")]
    [InlineData(-1250, "-1.2K")]
    [InlineData(int.MinValue, "-2147.4M")]
    public void Compact_score_truncates_like_ebay_search_cards(int score, string expected) =>
        Assert.Equal(expected, SellerReputation.CompactScore(score));

    [Fact]
    public async Task Http_pages_render_ebay_entry_points_for_each_role()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt) VALUES({s.Product},{s.Buyer},{s.Item},4,NULL,SYSUTCDATETIME())");
        var other = await Place(s); // Pending: not ready for feedback, still has More actions.
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var seller = await Login(factory, "seller");

        static string Between(string html, string from, string to) { var i = html.IndexOf(from, StringComparison.Ordinal); return html[i..html.IndexOf(to, i, StringComparison.Ordinal)]; }
        static void Current(string html, string label) => Assert.Matches($"<a[^>]*aria-current=\"page\"[^>]*>{label}</a>", html);

        // A2: stars + count and seller line on catalog cards (no feedback yet → score only).
        var catalog = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?q=Wireless%20Headphones"));
        Assert.Contains("aria-label=\"4.0 out of 5 stars, 1 product rating\"", catalog);
        Assert.Matches(@"<span class=""seller-caption-name"">seller</span>\s*<span title=""Feedback score"">\(0\)</span>", catalog);
        Assert.DoesNotContain("card-rating", WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?q=Desk%20Lamp")));

        // A1: Message lives in the seller card; the old standalone link is gone.
        var anonDetail = await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}");
        var anonCard = Between(anonDetail, "class=\"seller-section\"", "class=\"detail-price\"");
        Assert.Contains("class=\"button outline seller-message\"", anonCard); Assert.Contains("returnUrl=", anonCard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contact seller", anonDetail);
        var buyerCard = Between(await buyer.GetStringAsync($"/Catalog/Details/{s.Product}"), "class=\"seller-section\"", "class=\"detail-price\"");
        Assert.Contains($"data-chat-product-id=\"{s.Product}\">Message</button>", buyerCard);
        Assert.DoesNotContain("seller-message", await seller.GetStringAsync($"/Catalog/Details/{s.Product}"));

        // A3/A4: chips, Leave feedback straight to the R1 form for a single open line, More actions → Contact seller.
        var purchases = await buyer.GetStringAsync("/Orders/Purchases");
        Current(purchases, "All Purchases");
        Assert.Contains("href=\"/Orders/Purchases?status=ReadyForFeedback\"", purchases);
        Assert.Contains($"href=\"/Reviews/Create?orderItemId={s.Item}\">Leave feedback</a>", purchases);
        Assert.Single(Regex.Matches(purchases, ">Leave feedback</a>"));
        Assert.Equal(2, Regex.Matches(purchases, "<summary class=\"button outline\">More actions</summary>").Count);
        Assert.Contains($"data-chat-order-item-id=\"{s.Item}\">Contact seller</button>", purchases);
        Assert.Contains($"data-chat-order-item-id=\"{FirstLine(s, other)}\">Contact seller</button>", purchases);
        Assert.Contains("id=\"chat-panel\"", purchases);
        var ready = await buyer.GetStringAsync("/Orders/Purchases?status=ReadyForFeedback");
        Current(ready, "Ready for feedback");
        Assert.Contains($"Order #{s.Order}", ready); Assert.DoesNotContain($"Order #{other}", ready);

        // Leaving seller feedback closes the last open part: the order leaves the filter and the card shows 100% positive.
        AddFeedback(s, s.Item);
        Assert.Contains("You have no orders ready for feedback.", await buyer.GetStringAsync("/Orders/Purchases?status=ReadyForFeedback"));
        Assert.DoesNotContain(">Leave feedback</a>", await buyer.GetStringAsync("/Orders/Purchases"));
        catalog = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?q=Wireless%20Headphones"));
        Assert.Matches(@"<span class=""seller-caption-name"">seller</span>\s*<span> 100% positive</span>\s*<span title=""Feedback score"">\(1\)</span>", catalog);

        var sales = await seller.GetStringAsync("/Orders/Sales");
        Current(sales, "All sales");
        Assert.DoesNotContain("Ready for feedback", sales); Assert.DoesNotContain("More actions", sales); Assert.DoesNotContain("Leave feedback", sales);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync("/Orders/Sales?status=ReadyForFeedback")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Orders/Purchases?status=Unknown")).StatusCode);

        // A5: folder panel toggle, collapsed by default.
        var messages = await buyer.GetStringAsync("/Messages");
        Assert.Contains("data-folder-toggle aria-controls=\"chat-folders\" aria-expanded=\"false\" aria-label=\"Show folders\"", messages);
        Assert.Contains("<aside id=\"chat-folders\" class=\"chat-folders\"", messages);
        Assert.Contains("<h1>Messages</h1>", messages);
    }
}
