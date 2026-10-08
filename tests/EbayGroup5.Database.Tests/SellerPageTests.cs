using System.Net;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Infrastructure.Catalog;
using EbayGroup5.Infrastructure.Reputation;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

// H02 (FEEDBACK.md, Quyết định H02): F2 profile queries, About-this-seller facts from clone data, item page and store links.
public sealed class SellerPageTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static FeedbackService Service(SetupData s) => new(Open(s.Target), new FixedClock(Now));

    private static int Product(SetupData s, string title, int? category = null) => int.Parse(SqlFixture.Scalar(s.Target,
        $"INSERT dbo.Product(title,price,categoryId,sellerId,isAuction) VALUES(N'{title}',5,{(category?.ToString() ?? "NULL")},{s.Seller},0); SELECT SCOPE_IDENTITY();"));

    private static int Feedback(SetupData s, int product, string sentiment, DateTime created, string? comment, int quantity = 1) => int.Parse(SqlFixture.Scalar(s.Target, $"""
        INSERT dbo.OrderTable(buyerId,addressId,orderDate,totalPrice,status,SellerId) VALUES({s.Buyer},{s.Address},'{created:yyyy-MM-ddTHH:mm:ss}',5,'Delivered',{s.Seller});
        INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES(SCOPE_IDENTITY(),{product},{quantity},5);
        INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment,CreatedAtUtc) VALUES(SCOPE_IDENTITY(),{s.Buyer},{s.Seller},'{sentiment}',{(comment == null ? "NULL" : $"N'{comment}'")},'{created:yyyy-MM-ddTHH:mm:ss}');
        SELECT SCOPE_IDENTITY();
        """));

    [Fact]
    public async Task Profile_query_filters_tabs_period_search_sort_and_page_size()
    {
        var s = await Setup();
        var lamp = Product(s, "Brass Desk Lamp");
        var cable = Product(s, "USB-C Cable");
        var a = Feedback(s, lamp, "Positive", Now.AddDays(-3), "Bright lamp");
        var b = Feedback(s, cable, "Negative", Now.AddDays(-40), null);
        var c = Feedback(s, cable, "Positive", Now.AddDays(-200), "Fast charging");
        var d = Feedback(s, lamp, "Neutral", Now.AddDays(-400), "Okay");
        var service = Service(s);
        async Task<int[]> Ids(FeedbackProfileQuery q) => (await service.ProfileAsync(s.Seller, q))!.Entries.Select(x => x.Id).ToArray();

        Assert.Equal(new[] { d, c, a, b }, await Ids(new())); // Most relevant: entries with a comment first, then newest.
        Assert.Equal(new[] { d, c, b, a }, await Ids(new(Sort: FeedbackSort.Recent)));
        Assert.Equal(new[] { a }, await Ids(new(Period: FeedbackPeriod.PastMonth)));
        Assert.Equal(new[] { a, b }.Order(), (await Ids(new(Period: FeedbackPeriod.Past6Months))).Order());
        Assert.Equal(new[] { c, a, b }.Order(), (await Ids(new(Period: FeedbackPeriod.PastYear))).Order());
        Assert.Equal(new[] { d, a }.Order(), (await Ids(new(Search: "lamp"))).Order());
        Assert.Equal(new[] { c, b }.Order(), (await Ids(new(Search: $"#{cable}"))).Order());
        Assert.Equal(new[] { c, b }.Order(), (await Ids(new(Search: cable.ToString()))).Order());
        Assert.Equal(new[] { b }, await Ids(new(Sentiment: FeedbackSentiment.Negative, Tab: FeedbackTab.Seller)));
        foreach (var tab in new[] { FeedbackTab.Buyer, FeedbackTab.Left })
        {
            var page = await service.ProfileAsync(s.Seller, new FeedbackProfileQuery(Tab: tab));
            Assert.Equal(0, page!.Total);
            Assert.Equal(4, page.Reputation.Total); // Header numbers stay the seller's.
        }
        var sized = await service.ProfileAsync(s.Seller, new FeedbackProfileQuery(PageSize: 2, Page: 2));
        Assert.Equal((4, 2, 2), (sized!.Total, sized.TotalPages, sized.Entries.Count));
        var entry = (await service.ProfileAsync(s.Seller, new FeedbackProfileQuery(Search: "Brass", Sentiment: FeedbackSentiment.Positive)))!.Entries.Single();
        Assert.Equal((lamp, 0, "b***r"), (entry.ProductId!.Value, entry.BuyerScore, entry.Buyer));

        foreach (var bad in new FeedbackProfileQuery[] { new(PageSize: 0), new(PageSize: 101), new(Page: 0), new(Search: new string('x', 101)),
                     new(Tab: (FeedbackTab)9), new(Period: (FeedbackPeriod)9), new(Sort: (FeedbackSort)9) })
            await Assert.ThrowsAsync<ArgumentException>(() => service.ProfileAsync(s.Seller, bad));
    }

    [Fact]
    public async Task Summary_uses_only_clone_data_for_joined_sold_categories_store_and_contact()
    {
        var s = await Setup();
        var service = Service(s);
        Assert.Null(await service.SummaryAsync(s.Buyer));
        Assert.Null(await service.SummaryAsync(int.MaxValue));
        var before = (await service.SummaryAsync(s.Seller))!;
        Assert.NotNull(before.JoinedAtUtc); // Fixture accounts are created after 0016, so the default applies.
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET JoinedAtUtc=NULL WHERE id={s.Seller}");
        Assert.Null((await service.SummaryAsync(s.Seller))!.JoinedAtUtc); // Pre-0016 accounts: unknown, not shown.
        var cat1 = int.Parse(SqlFixture.Scalar(s.Target, "INSERT dbo.Category(name) VALUES(N'Lighting'); SELECT SCOPE_IDENTITY();"));
        var cat2 = int.Parse(SqlFixture.Scalar(s.Target, "INSERT dbo.Category(name) VALUES(N'Cables'); SELECT SCOPE_IDENTITY();"));
        var lamp = Product(s, "Lamp", cat1); Product(s, "Lamp 2", cat1); var cable = Product(s, "Cable", cat2);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Product(title,price,categoryId,sellerId,isAuction) VALUES(N'Auction',5,{cat2},{s.Seller},1)");
        Feedback(s, lamp, "Positive", Now.AddDays(-1), null, quantity: 3);
        Feedback(s, cable, "Positive", Now.AddDays(-2), null, quantity: 1200);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Store(sellerId,storeName) VALUES({s.Seller},N'Demo Lights')");
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET JoinedAtUtc='2024-12-22T08:00:00' WHERE id={s.Seller}");

        var summary = (await service.SummaryAsync(s.Seller))!;
        Assert.Equal(new DateTime(2024, 12, 22, 8, 0, 0, DateTimeKind.Utc), summary.JoinedAtUtc);
        Assert.Equal(DateTimeKind.Utc, summary.JoinedAtUtc!.Value.Kind);
        Assert.Equal(before.ItemsSold + 1203, summary.ItemsSold);
        Assert.Equal("Demo Lights", summary.StoreName);
        Assert.Equal(cable, summary.ContactProductId); // Newest fixed-price listing; the auction is not a listing.
        Assert.Equal("Lighting", summary.Categories[0].Name);
        Assert.Equal(2, summary.Categories[0].Listings);
        Assert.Contains(summary.Categories, x => x.Name == "Cables" && x.Listings == 1);

        var created = int.Parse(SqlFixture.Scalar(s.Target, "INSERT dbo.[User](username,email,password,role) VALUES(N'Newbie',N'newbie@h02.example.invalid',N'x',N'Seller'); SELECT SCOPE_IDENTITY();"));
        Assert.NotNull((await service.SummaryAsync(created))!.JoinedAtUtc); // Default for new accounts.

        var page = await new CatalogService(Open(s.Target)).SearchAsync(null, null, 1, s.Seller);
        Assert.Equal("Demo Lights", page.SellerName);
        Assert.All(page.Items, x => Assert.Equal(s.Seller, x.SellerId));
        Assert.Null((await new CatalogService(Open(s.Target)).SearchAsync(null, null, 1, s.Buyer)).SellerName);
    }

    private static void Message(SetupData s, int buyer, int conversation, int sequence, bool fromBuyer, DateTime sent)
    {
        var (from, to) = fromBuyer ? (buyer, s.Seller) : (s.Seller, buyer);
        SqlFixture.Execute(s.Target, $"""
            INSERT dbo.Message(senderId,receiverId,content,[timestamp],ConversationId,ConversationBuyerId,ConversationSellerId,Sequence,ClientMessageId,PayloadHash,SentAtUtc,TraceId)
            VALUES({from},{to},N'hello','{sent:yyyy-MM-ddTHH:mm:ss}',{conversation},{buyer},{s.Seller},{sequence},NEWID(),0x{new string('0', 64)},'{sent:yyyy-MM-ddTHH:mm:ss}','{new string('a', 32)}');
            UPDATE dbo.ChatConversation SET LastSequence={sequence} WHERE Id={conversation};
            """);
    }

    [Fact]
    public async Task Response_time_uses_the_median_of_buyer_turns_in_ninety_days()
    {
        var s = await Setup();
        int Conversation(int buyer) => int.Parse(SqlFixture.Scalar(s.Target, $"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({buyer},{s.Seller}); SELECT SCOPE_IDENTITY();"));
        bool Responds() => Service(s).SummaryAsync(s.Seller).GetAwaiter().GetResult()!.RespondsWithin24Hours;
        Assert.False(Responds()); // No turns: not shown.
        var first = Conversation(s.Buyer);
        Message(s, s.Buyer, first, 1, true, Now.AddDays(-10));
        Message(s, s.Buyer, first, 2, true, Now.AddDays(-10).AddMinutes(5));  // Same turn as the previous message.
        Message(s, s.Buyer, first, 3, false, Now.AddDays(-10).AddHours(2));   // Answered in 2 h.
        Assert.True(Responds());
        Message(s, s.Buyer, first, 4, true, Now.AddHours(-3));                // Open but less than 24 h old: not counted yet.
        Assert.True(Responds());
        var second = Conversation(s.OtherBuyer);
        Message(s, s.OtherBuyer, second, 1, true, Now.AddDays(-6));
        Message(s, s.OtherBuyer, second, 2, false, Now.AddDays(-4));          // Answered in 48 h.
        Message(s, s.OtherBuyer, second, 3, true, Now.AddDays(-3));           // Unanswered for more than 24 h: slow.
        Assert.False(Responds());                                             // Median of 2 h, 48 h, unanswered = 48 h.
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Message SET SentAtUtc=DATEADD(day,-100,SentAtUtc) WHERE ConversationId={second}");
        Assert.True(Responds());                                              // Older than 90 days: ignored.
    }

    [Fact]
    public async Task Http_profile_item_page_store_and_demo_notice_render_ebay_blocks()
    {
        var s = await Setup();
        Feedback(s, s.Product, "Positive", DateTime.UtcNow.AddDays(-2), "Lovely headphones");
        Feedback(s, s.Product, "Negative", DateTime.UtcNow.AddDays(-30), "Late");
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        var path = $"/Sellers/Feedback/{s.Seller}";

        var profile = WebUtility.HtmlDecode(await anonymous.GetStringAsync(path));
        foreach (var text in new[] { "Feedback profile", "Member Quick Links", "View items for sale", "Feedback ratings", "Detailed seller ratings",
                     "Average for the last 12 months", "at least 10 detailed seller ratings", "All received Feedback", "Received as buyer", "Received as seller",
                     "Left for others", "Search Feedback received as seller with an item title or ID:", "Most relevant", "Items per page:", "b***r (0)", $"(#{s.Product})" })
            Assert.Contains(text, profile);
        Assert.DoesNotContain("All time", profile);
        Assert.Contains("No feedback received as a buyer yet.", await anonymous.GetStringAsync(path + "?tab=buyer"));
        Assert.Contains("Lovely headphones", await anonymous.GetStringAsync(path + $"?q=%23{s.Product}&period=1&sort=recent&size=50"));
        Assert.DoesNotContain("Late", await anonymous.GetStringAsync(path + "?period=1"));
        foreach (var query in new[] { "?tab=x", "?period=3", "?sort=old", "?size=30", "?q=" + new string('x', 101), "?page=0" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync(path + query)).StatusCode);

        var item = WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Catalog/Details/{s.Product}"));
        foreach (var text in new[] { "About this seller", "Visit store", "Message seller", "Save seller", "Seller feedback", "See all feedback",
                     "Seller's other items", "Contact seller", "items sold", "Lovely headphones" })
            Assert.Contains(text, item);
        Assert.Contains($"/Catalog?seller={s.Seller}", item);
        Assert.Contains("/Help/Unavailable/save-seller", item);
        Assert.DoesNotContain("Lovely headphones", await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}?fb=negative"));
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync($"/Catalog/Details/{s.Product}?fb=bogus")).StatusCode);

        Assert.Contains("Items for sale from", await anonymous.GetStringAsync($"/Catalog?seller={s.Seller}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/Catalog?seller=0")).StatusCode);
        var notice = await anonymous.GetAsync("/Help/Unavailable/save-seller");
        Assert.Equal(HttpStatusCode.OK, notice.StatusCode);
        Assert.Contains("This feature isn't available in this demo", WebUtility.HtmlDecode(await notice.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/Help/Unavailable/delete-everything")).StatusCode);
    }
}
