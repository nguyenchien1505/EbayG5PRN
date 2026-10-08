using System.Net;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Infrastructure.Reputation;
using Microsoft.Data.SqlClient;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

// H01 (FEEDBACK.md, Quyết định H01): buyer-week score rule, detailed seller ratings, feedback stars and the R1 form.
public sealed class ReputationRulesTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static FeedbackService Service(SetupData s, DateTime? now = null, int? minimum = null) =>
        new(Open(s.Target), new FixedClock(now ?? Now), minimum is int m ? new FeedbackDisplayOptions { DetailedRatingsMinimum = m } : null);

    // One delivered order per row so each feedback carries its own transaction date (orderDate is UTC, NULL allowed).
    private static int Add(SetupData s, int buyer, string? orderedUtc, string sentiment, DateTime? created = null, string details = "NULL,NULL,NULL,NULL", bool hidden = false)
    {
        var ordered = orderedUtc == null ? "NULL" : $"'{orderedUtc}'";
        return int.Parse(SqlFixture.Scalar(s.Target, $"""
            INSERT dbo.OrderTable(buyerId,addressId,orderDate,totalPrice,status,SellerId) VALUES({buyer},{s.Address},{ordered},10,'Delivered',{s.Seller});
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES(SCOPE_IDENTITY(),{s.Product},1,10);
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,CreatedAtUtc,ArrivedOnTime,DescriptionRating,ShippingTimeRating,CommunicationRating,HiddenAtUtc)
                VALUES(SCOPE_IDENTITY(),{buyer},{s.Seller},'{sentiment}','{created ?? Now.AddDays(-3):yyyy-MM-ddTHH:mm:ss}',{details},{(hidden ? "SYSUTCDATETIME()" : "NULL")});
            SELECT SCOPE_IDENTITY();
            """));
    }

    [Fact]
    public async Task Score_counts_each_buyer_once_per_pacific_week_including_dst_changes()
    {
        var s = await Setup();
        Add(s, s.Buyer, "2026-09-07T06:59:00", "Positive");      // Sun 6 Sep 23:59 PDT -> week of 31 Aug.
        Add(s, s.Buyer, "2026-09-07T07:00:00", "Positive");      // Mon 7 Sep 00:00 PDT -> week of 7 Sep.
        Add(s, s.Buyer, "2026-09-13T06:59:00", "Positive");      // Sun 13 Sep 23:59 PDT -> same week, not counted again.
        Add(s, s.Buyer, null, "Positive", new DateTime(2026, 9, 8, 12, 0, 0)); // No order date: feedback date, same week again.
        Add(s, s.OtherBuyer, "2026-09-07T07:00:00", "Positive"); // Another buyer in that week counts.
        Add(s, s.Buyer, "2026-10-26T08:00:00", "Positive");      // Mon 26 Oct 01:00 PDT.
        Add(s, s.Buyer, "2026-11-02T07:30:00", "Positive");      // Sun 1 Nov 23:30 PST (after DST ended): still the week of 26 Oct.
        Add(s, s.Buyer, "2026-09-08T10:00:00", "Negative");
        Add(s, s.Buyer, "2026-09-09T10:00:00", "Negative");      // Same buyer-week as the previous negative.
        Add(s, s.Buyer, "2026-09-09T10:00:00", "Neutral");
        Add(s, s.OtherBuyer, "2026-09-09T10:00:00", "Negative", hidden: true); // Hidden by moderation: never counted.

        var rep = await Service(s).ReputationAsync(s.Seller);
        Assert.Equal(new FeedbackCounts(7, 1, 2), rep.Lifetime);
        Assert.Equal(new FeedbackCounts(4, 1, 1), rep.Unique);
        Assert.Equal(3, rep.Score);
        Assert.Equal(80m, rep.PositivePercent);
        Assert.Equal(FeedbackStar.For(3), rep.Star);
        Assert.Null(rep.Star);

        // 12-month % keeps the buyer-week rule but only for feedback left in the window; the lifetime score does not expire.
        var later = await Service(s, Now.AddMonths(12).AddDays(1)).ReputationAsync(s.Seller);
        Assert.Equal(FeedbackCounts.Empty, later.Unique12);
        Assert.Null(later.PositivePercent);
        Assert.Equal(3, later.Score);
    }

    [Fact]
    public async Task Detailed_seller_ratings_average_twelve_months_and_ship_free_counts_five_stars()
    {
        var s = await Setup();
        Add(s, s.Buyer, "2026-09-07T10:00:00", "Positive", details: "1,5,4,5");
        Add(s, s.Buyer, "2026-09-14T10:00:00", "Positive", details: "0,4,NULL,4");
        Add(s, s.Buyer, "2026-09-21T10:00:00", "Neutral", details: "NULL,4,3,NULL");
        Add(s, s.Buyer, "2026-09-28T10:00:00", "Positive");                                   // No details: shipping cost only.
        Add(s, s.Buyer, "2026-09-28T10:00:00", "Negative", details: "NULL,1,1,1", hidden: true); // Hidden: ignored.
        Add(s, s.Buyer, "2025-01-05T10:00:00", "Positive", Now.AddMonths(-13), details: "NULL,1,1,1"); // Older than 12 months.

        var details = (await Service(s).ReputationAsync(s.Seller)).Details;
        Assert.Equal(new DetailRating(4.3m, 3), details.Description);   // (5+4+4)/3 = 4.33
        Assert.Equal(new DetailRating(3.5m, 2), details.ShippingSpeed); // (4+3)/2
        Assert.Equal(new DetailRating(4.5m, 2), details.Communication); // (5+4)/2
        Assert.Equal(new DetailRating(5m, 4), details.ShippingCost);    // Every visible feedback in 12 months.
        Assert.Equal(7, details.BuyerRated);
        Assert.Equal(DetailedSellerRatings.Empty, (await Service(s).ReputationAsync(s.OtherSeller)).Details);

        Assert.Equal(10, Service(s).DetailedRatingsMinimum);
        Assert.Equal(3, Service(s, minimum: 3).DetailedRatingsMinimum);
        Assert.Throws<ArgumentOutOfRangeException>(() => Service(s, minimum: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Service(s, minimum: 1_001));
    }

    [Theory]
    [InlineData(-5, null, false)]
    [InlineData(9, null, false)]
    [InlineData(10, FeedbackStarColor.Yellow, false)]
    [InlineData(49, FeedbackStarColor.Yellow, false)]
    [InlineData(50, FeedbackStarColor.Blue, false)]
    [InlineData(100, FeedbackStarColor.Turquoise, false)]
    [InlineData(499, FeedbackStarColor.Turquoise, false)]
    [InlineData(500, FeedbackStarColor.Purple, false)]
    [InlineData(1_000, FeedbackStarColor.Red, false)]
    [InlineData(4_999, FeedbackStarColor.Red, false)]
    [InlineData(5_000, FeedbackStarColor.Green, false)]
    [InlineData(10_000, FeedbackStarColor.Yellow, true)]
    [InlineData(25_000, FeedbackStarColor.Turquoise, true)]
    [InlineData(50_000, FeedbackStarColor.Purple, true)]
    [InlineData(100_000, FeedbackStarColor.Red, true)]
    [InlineData(500_000, FeedbackStarColor.Green, true)]
    [InlineData(1_000_000, FeedbackStarColor.Silver, true)]
    public void Feedback_star_follows_the_ebay_score_tiers(int score, FeedbackStarColor? color, bool shooting)
    {
        var star = FeedbackStar.For(score);
        Assert.Equal(color, star?.Color);
        if (star != null) Assert.Equal(shooting, star.Shooting);
    }

    [Fact]
    public async Task R1_form_posts_details_with_one_comment_and_keeps_them_immutable()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var buyer = await Login(factory, "buyer");
        await Deliver(s, s.Order);
        var create = $"/Reviews/Create?orderItemId={s.Item}";
        var page = WebUtility.HtmlDecode(await buyer.GetStringAsync(create));
        foreach (var text in new[] { "How was your experience?", "How would you rate this item?", "Tell us more about this item", "Feedback length",
                     "Did this item arrive on time?", "Rate more details about your purchase", "Item description", "Shipping time", "Seller communication" })
            Assert.Contains(text, page);
        Assert.DoesNotContain("name=\"SellerComment\"", page);

        async Task<HttpResponseMessage> Post(Dictionary<string, string> form)
        {
            form["OrderItemId"] = s.Item.ToString();
            form["__RequestVerificationToken"] = Field(await buyer.GetStringAsync(create));
            return await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(form));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new() { ["Sentiment"] = "Positive", ["DescriptionRating"] = "7" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new() { ["Rating"] = "5", ["CommunicationRating"] = "5" })).StatusCode); // Details need the seller part.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new() { ["Sentiment"] = "Positive", ["ArrivedOnTime"] = "maybe" })).StatusCode);
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.SellerFeedback"));

        var posted = await Post(new()
        {
            ["Sentiment"] = "Positive", ["Rating"] = "5", ["Comment"] = "  Exactly as described  ", ["ArrivedOnTime"] = "true",
            ["DescriptionRating"] = "5", ["ShippingTimeRating"] = "4", ["CommunicationRating"] = "3"
        });
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal("Exactly as described|1|5|4|3", SqlFixture.Scalar(s.Target,
            "SELECT CONCAT(Comment,'|',CAST(ArrivedOnTime AS int),'|',DescriptionRating,'|',ShippingTimeRating,'|',CommunicationRating) FROM dbo.SellerFeedback"));
        Assert.Equal("5|Exactly as described", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(rating,'|',comment) FROM dbo.Review WHERE OrderItemId={s.Item}"));

        // The buyer-week key is stored at insert: Monday of the order's week in Pacific time.
        var ordered = DateTime.SpecifyKind(DateTime.Parse(SqlFixture.Scalar(s.Target, $"SELECT CONVERT(varchar(33),orderDate,126) FROM dbo.OrderTable WHERE id={s.Order}"),
            System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
        var pacific = TimeZoneInfo.ConvertTimeFromUtc(ordered, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles")).Date;
        var monday = pacific.AddDays(-(((int)pacific.DayOfWeek + 6) % 7));
        Assert.Equal(monday.ToString("yyyy-MM-dd"), SqlFixture.Scalar(s.Target, "SELECT CONVERT(varchar(10),TransactionWeek,23) FROM dbo.SellerFeedback"));
        foreach (var column in new[] { "ArrivedOnTime=0", "DescriptionRating=1", "ShippingTimeRating=NULL", "CommunicationRating=5", "TransactionWeek=NULL", "TransactionWeek='2000-01-03'" })
        {
            var ex = Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET {column}"));
            Assert.Equal(51009, ex.Number);
        }
        var bad = Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target,
            $"""
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,10);
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,DescriptionRating) VALUES(SCOPE_IDENTITY(),{s.Buyer},{s.Seller},'Positive',6);
            """));
        Assert.Equal(547, bad.Number); // CHECK constraint.
    }
}
