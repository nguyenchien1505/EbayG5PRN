using System.Globalization;
using System.Net;
using DbUp.Engine;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Infrastructure.Reputation;
using EbayGroup5.Infrastructure.Reviews;
using Microsoft.Data.SqlClient;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

public sealed class FeedbackTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly DateTime DeliveredAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    // Delivered via B06, then pins the delivery timestamp so time-window tests are deterministic.
    private static async Task<SetupData> Delivered(int extraLines = 0)
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Payment SET paidAt='{DeliveredAt:yyyy-MM-ddTHH:mm:ss}' WHERE orderId={s.Order}");
        for (var i = 0; i < extraLines; i++) SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90)");
        return s;
    }
    private static int[] Lines(SetupData s) =>
        SqlFixture.Rows(s.Target, $"SELECT CONVERT(varchar(12),id) FROM dbo.OrderItem WHERE orderId={s.Order}").Select(int.Parse).Order().ToArray();
    private static FeedbackService Service(SetupData s, DateTime now) => new(Open(s.Target), new FixedClock(now));
    private static LeaveFeedbackRequest Seller(int item, FeedbackSentiment sentiment, string? comment = null) => new(item, sentiment, comment, null, null);
    private static string Count(SetupData s) => SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.SellerFeedback");

    [Theory]
    [InlineData(0, 0, 0, 0, null)]
    [InlineData(0, 2, 0, 0, null)]
    [InlineData(1, 0, 0, 1, "100")]
    [InlineData(5, 3, 0, 5, "100")]
    [InlineData(3, 1, 1, 2, "75")]
    [InlineData(2, 0, 1, 1, "66.7")]
    [InlineData(1, 0, 2, -1, "33.3")]
    [InlineData(199, 4, 1, 198, "99.5")]
    [InlineData(1, 0, 15, -14, "6.3")]
    public void Formula_is_positive_minus_negative_and_excludes_neutral_from_percentage(int positive, int neutral, int negative, int score, string? percent)
    {
        var reputation = new SellerReputation(positive, neutral, negative);
        Assert.Equal(score, reputation.Score);
        Assert.Equal(positive + neutral + negative, reputation.Total);
        Assert.Equal(percent == null ? null : decimal.Parse(percent, CultureInfo.InvariantCulture), reputation.PositivePercent);
    }

    [Fact]
    public async Task Time_windows_allow_positive_first_then_all_sentiments_until_day_sixty()
    {
        var s = await Delivered(extraLines: 5);
        var lines = Lines(s);
        var early = DeliveredAt.AddDays(1);
        var line = await Service(s, early).FindItemAsync(s.Buyer, lines[0]);
        Assert.True(line!.CanLeave);
        Assert.True(line.PositiveOnly);
        Assert.Equal(DeliveredAt.AddDays(7), line.NeutralNegativeFromUtc);
        Assert.Equal(DeliveredAt.AddDays(60), line.DeadlineUtc);
        foreach (var sentiment in new[] { FeedbackSentiment.Negative, FeedbackSentiment.Neutral })
        {
            var blocked = await Service(s, early).SubmitAsync(s.Buyer, Seller(lines[0], sentiment, "too soon"));
            Assert.Equal(LeaveFeedbackOutcome.Conflict, blocked.Outcome);
            Assert.Contains("wait 7 days", blocked.Error);
        }
        Assert.Equal(LeaveFeedbackOutcome.Conflict, (await Service(s, DeliveredAt.AddDays(7).AddSeconds(-1)).SubmitAsync(s.Buyer, Seller(lines[0], FeedbackSentiment.Negative))).Outcome);
        Assert.Equal("0", Count(s));
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, early).SubmitAsync(s.Buyer, Seller(lines[0], FeedbackSentiment.Positive, "  Fast shipping  "))).Outcome);
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, DeliveredAt.AddDays(7)).SubmitAsync(s.Buyer, Seller(lines[1], FeedbackSentiment.Negative))).Outcome);
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, DeliveredAt.AddDays(30)).SubmitAsync(s.Buyer, Seller(lines[2], FeedbackSentiment.Neutral))).Outcome);
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, DeliveredAt.AddDays(60)).SubmitAsync(s.Buyer, Seller(lines[3], FeedbackSentiment.Positive))).Outcome);
        var late = await Service(s, DeliveredAt.AddDays(60).AddSeconds(1)).SubmitAsync(s.Buyer, Seller(lines[4], FeedbackSentiment.Positive));
        Assert.Equal(LeaveFeedbackOutcome.Conflict, late.Outcome);
        Assert.Contains("60-day", late.Error);
        Assert.False((await Service(s, DeliveredAt.AddDays(61)).FindItemAsync(s.Buyer, lines[4]))!.CanLeave);
        Assert.Equal("4", Count(s));
        Assert.Equal($"{s.Seller}|{s.Buyer}|Positive|Fast shipping", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(SellerId,'|',BuyerId,'|',Sentiment,'|',Comment) FROM dbo.SellerFeedback WHERE OrderItemId={lines[0]}"));
        Assert.Equal(new SellerReputation(2, 1, 1), await Service(s, early).ReputationAsync(s.Seller));
        var left = (await Service(s, early).ListForOrderAsync(s.Buyer, s.Order))!;
        Assert.Equal(new FeedbackSentiment?[] { FeedbackSentiment.Positive, FeedbackSentiment.Negative, FeedbackSentiment.Neutral, FeedbackSentiment.Positive, null, null },
            left.Select(x => x.Sentiment).ToArray());
    }

    [Fact]
    public async Task Ineligible_owner_role_state_and_input_are_rejected_without_rows()
    {
        var s = await Setup();
        var now = DateTime.UtcNow;
        Assert.Equal(LeaveFeedbackOutcome.Conflict, (await Service(s, now).SubmitAsync(s.Buyer, Seller(s.Item, FeedbackSentiment.Positive))).Outcome); // Not delivered.
        await Deliver(s, s.Order);
        var later = DateTime.UtcNow.AddDays(10);
        Assert.Equal(LeaveFeedbackOutcome.NotFound, (await Service(s, later).SubmitAsync(s.OtherBuyer, Seller(s.Item, FeedbackSentiment.Positive))).Outcome);
        Assert.Equal(LeaveFeedbackOutcome.NotFound, (await Service(s, later).SubmitAsync(s.Buyer, Seller(int.MaxValue, FeedbackSentiment.Positive))).Outcome);
        foreach (var actor in new[] { s.Seller, s.OtherSeller, s.Admin, int.MaxValue })
            Assert.Equal(LeaveFeedbackOutcome.Forbidden, (await Service(s, later).SubmitAsync(actor, Seller(s.Item, FeedbackSentiment.Positive))).Outcome);
        Assert.Null(await Service(s, later).FindItemAsync(s.OtherBuyer, s.Item));
        Assert.Null(await Service(s, later).ListForOrderAsync(s.Seller, s.Order));
        foreach (var bad in new LeaveFeedbackRequest[]
        {
            new(s.Item, null, null, null, null),
            new(s.Item, null, "comment without sentiment", null, null),
            new(s.Item, null, null, null, "item comment without stars"),
            new(s.Item, (FeedbackSentiment)9, null, null, null),
            new(s.Item, FeedbackSentiment.Positive, new string('x', FeedbackLimits.MaxCommentLength + 1), null, null),
            new(s.Item, FeedbackSentiment.Positive, "bell\a", null, null),
            new(s.Item, FeedbackSentiment.Positive, null, 6, null),
        })
            Assert.Equal(LeaveFeedbackOutcome.Invalid, (await Service(s, later).SubmitAsync(s.Buyer, bad)).Outcome);

        SqlFixture.Execute(s.Target, $"UPDATE dbo.Payment SET paidAt=NULL WHERE orderId={s.Order}");
        var noDate = await Service(s, later).SubmitAsync(s.Buyer, Seller(s.Item, FeedbackSentiment.Positive));
        Assert.Equal(LeaveFeedbackOutcome.Conflict, noDate.Outcome);
        Assert.Contains("delivery date", noDate.Error);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Payment SET paidAt=SYSUTCDATETIME() WHERE orderId={s.Order}; UPDATE dbo.OrderTable SET SellerId=NULL WHERE id={s.Order}");
        Assert.Contains("Seller details", (await Service(s, later).SubmitAsync(s.Buyer, Seller(s.Item, FeedbackSentiment.Positive))).Error);
        Assert.Equal("0", Count(s));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET SellerId={s.Seller} WHERE id={s.Order}");
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, later).SubmitAsync(s.Buyer, Seller(s.Item, FeedbackSentiment.Neutral, " \t "))).Outcome);
        var again = await Service(s, later).SubmitAsync(s.Buyer, Seller(s.Item, FeedbackSentiment.Positive, "change"));
        Assert.Equal(LeaveFeedbackOutcome.Conflict, again.Outcome);
        Assert.Equal("Neutral|", SqlFixture.Scalar(s.Target, "SELECT CONCAT(Sentiment,'|',Comment) FROM dbo.SellerFeedback"));
    }

    [Fact]
    public async Task Combined_submission_is_atomic_and_credits_the_order_seller_snapshot()
    {
        var s = await Delivered(extraLines: 1);
        var lines = Lines(s);
        var now = DeliveredAt.AddDays(10);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Product SET sellerId={s.OtherSeller} WHERE id={s.Product}");
        await using (var db = Open(s.Target))
            Assert.Equal(EbayGroup5.Application.Reviews.ReviewOutcome.Success, (await new ReviewService(db).SubmitAsync(s.Buyer, lines[0], 5, "reviewed first")).Outcome);
        var rolledBack = await Service(s, now).SubmitAsync(s.Buyer, new(lines[0], FeedbackSentiment.Negative, "should roll back", 1, "duplicate review"));
        Assert.Equal(LeaveFeedbackOutcome.Conflict, rolledBack.Outcome);
        Assert.Equal("0", Count(s));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.Review"));

        var both = await Service(s, now).SubmitAsync(s.Buyer, new(lines[1], FeedbackSentiment.Positive, "Great seller", 4, "Nice item"));
        Assert.Equal(LeaveFeedbackOutcome.Success, both.Outcome);
        Assert.True(both.FeedbackPosted && both.ReviewPosted);
        Assert.Equal(s.Order, both.OrderId);
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, now).SubmitAsync(s.Buyer, Seller(lines[0], FeedbackSentiment.Negative))).Outcome);
        Assert.Equal(new SellerReputation(1, 0, 1), await Service(s, now).ReputationAsync(s.Seller));
        Assert.Equal(SellerReputation.Empty, await Service(s, now).ReputationAsync(s.OtherSeller));
        Assert.Equal("2", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.SellerFeedback WHERE SellerId={s.Seller}"));
        Assert.Equal("4|Nice item", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(rating,'|',comment) FROM dbo.Review WHERE OrderItemId={lines[1]}"));
    }

    [Fact]
    public async Task Concurrent_submissions_store_one_feedback_and_write_failures_roll_back()
    {
        var s = await Delivered(extraLines: 2);
        var now = DeliveredAt.AddDays(10);
        foreach (var id in Lines(s).Take(2))
        {
            var results = await Task.WhenAll(Service(s, now).SubmitAsync(s.Buyer, Seller(id, FeedbackSentiment.Positive)), Service(s, now).SubmitAsync(s.Buyer, Seller(id, FeedbackSentiment.Negative)));
            Assert.Single(results, x => x.Outcome == LeaveFeedbackOutcome.Success);
            Assert.Single(results, x => x.Outcome == LeaveFeedbackOutcome.Conflict);
        }
        Assert.Equal("2", Count(s));
        var last = Lines(s)[2];
        SqlFixture.Execute(s.Target, "CREATE TRIGGER dbo.FeedbackFailure ON dbo.SellerFeedback AFTER INSERT AS THROW 51008, 'Isolated test failure', 1;");
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => Service(s, now).SubmitAsync(s.Buyer, new(last, FeedbackSentiment.Positive, null, 5, null)));
        Assert.Equal("2", Count(s));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Review WHERE OrderItemId={last}"));
        SqlFixture.Execute(s.Target, "DISABLE TRIGGER dbo.FeedbackFailure ON dbo.SellerFeedback");
        Assert.Equal(LeaveFeedbackOutcome.Success, (await Service(s, now).SubmitAsync(s.Buyer, new(last, FeedbackSentiment.Positive, null, 5, null))).Outcome);
    }

    [Fact]
    public async Task Schema_constraints_and_app_permissions_protect_feedback_rows()
    {
        var s = await Setup();
        string Insert(string item, string buyer, string seller, string sentiment, string comment) =>
            $"INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment) VALUES({item},{buyer},{seller},{sentiment},{comment})";
        foreach (var sql in new[]
        {
            Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'positive'", "NULL"),
            Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'Great'", "NULL"),
            Insert(s.Item.ToString(), s.Buyer.ToString(), s.Buyer.ToString(), "'Positive'", "NULL"),
            Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'Positive'", "N'   '"),
            Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'Positive'", "REPLICATE(N'x',501)"),
            Insert("2147483647", s.Buyer.ToString(), s.Seller.ToString(), "'Positive'", "NULL"),
            Insert(s.Item.ToString(), "NULL", s.Seller.ToString(), "'Positive'", "NULL"),
        })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, sql));
        SqlFixture.Execute(s.Target, Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'Negative'", "N'ok'"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Insert(s.Item.ToString(), s.Buyer.ToString(), s.Seller.ToString(), "'Positive'", "NULL")));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.SellerFeedback WHERE CreatedAtUtc IS NOT NULL"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_SellerFeedback_Sentiment' AND is_not_trusted=0"));

        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        Assert.Equal("1", SqlFixture.Scalar(app, "SELECT COUNT(*) FROM dbo.SellerFeedback"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "UPDATE dbo.SellerFeedback SET Sentiment='Positive'"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "DELETE dbo.SellerFeedback"));
    }

    [Fact]
    public void Migration_adds_feedback_table_and_leaves_source_feedback_rows_untouched()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        var scripts = new List<SqlScript> { new("0001_Baseline.sql", Baseline.ReadTableDdl()) };
        var assembly = typeof(MigrationRunner).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".Scripts.") && !x.EndsWith("0008_SellerFeedback.sql")).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..], reader.ReadToEnd()));
        }
        Assert.True(MigrationRunner.BuildEngine(target, scripts).PerformUpgrade().Successful);
        DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.Feedback(sellerId,averageRating,totalReviews,positiveRate) SELECT id,4.50,10,90.00 FROM dbo.[User] WHERE role=N'Seller'; INSERT dbo.Feedback(sellerId) VALUES(NULL)");
        var metadata = SqlFixture.Rows(target, SqlFixture.Metadata);
        const string legacy = "SELECT CONCAT(id,'|',sellerId,'|',averageRating,'|',totalReviews,'|',positiveRate) FROM dbo.Feedback";
        var before = SqlFixture.Rows(target, legacy);
        Assert.Equal(1, MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.True(before.SetEquals(SqlFixture.Rows(target, legacy)));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.SellerFeedback"));
        Assert.Equal(0, MigrationRunner.Run(target));
        Assert.False(DemoSeedRunner.Run(target));
    }

    [Fact]
    public async Task Http_leave_feedback_page_enforces_wait_window_and_updates_seller_badge()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var stranger = await Login(factory, "otherbuyer");
        Assert.Contains("No feedback yet", await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}"));
        await Deliver(s, s.Order);
        var create = $"/Reviews/Create?orderItemId={s.Item}";
        Assert.Contains("Leave feedback", await buyer.GetStringAsync($"/Orders/Details/{s.Order}"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(create)).StatusCode);
        var page = await buyer.GetStringAsync(create);
        Assert.Contains("How was your experience?", page);
        Assert.Contains("How would you rate this item?", page);
        Assert.Contains("wait 7 days before you can leave neutral or negative feedback", page);
        Assert.Matches("id=\"sentiment-Negative\"[^>]*disabled", page);
        Assert.DoesNotMatch("id=\"sentiment-Positive\"[^>]*disabled", page);
        Assert.Contains("0 / 500", System.Text.RegularExpressions.Regex.Replace(page, "<[^>]+>", ""));

        async Task<HttpResponseMessage> Post(Dictionary<string, string> form)
        {
            form["OrderItemId"] = s.Item.ToString();
            form["__RequestVerificationToken"] = Field(await buyer.GetStringAsync(create));
            return await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(form));
        }
        var early = await Post(new() { ["Sentiment"] = "Negative", ["SellerComment"] = "keep me" });
        Assert.Equal((HttpStatusCode)409, early.StatusCode);
        var earlyHtml = await early.Content.ReadAsStringAsync();
        Assert.Contains("wait 7 days", earlyHtml);
        Assert.Contains("keep me", earlyHtml);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new() { ["Sentiment"] = "Great" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new() { ["SellerComment"] = "no sentiment" })).StatusCode);
        Assert.Equal("0", Count(s));

        SqlFixture.Execute(s.Target, $"UPDATE dbo.Payment SET paidAt=DATEADD(day,-10,SYSUTCDATETIME()) WHERE orderId={s.Order}");
        Assert.DoesNotMatch("id=\"sentiment-Negative\"[^>]*disabled", await buyer.GetStringAsync(create));
        var posted = await Post(new() { ["Sentiment"] = "Positive", ["SellerComment"] = "Quick replies", ["SellerId"] = s.OtherSeller.ToString(), ["BuyerId"] = s.OtherBuyer.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        var after = WebUtility.HtmlDecode(await buyer.GetStringAsync(posted.Headers.Location!.OriginalString));
        Assert.Contains("Thanks — your feedback was posted.", after);
        Assert.Contains("You left Positive feedback for the seller.", after);
        Assert.Contains("Leave feedback", after); // Item review still open.
        Assert.Equal($"{s.Seller}|{s.Buyer}|Positive|Quick replies", SqlFixture.Scalar(s.Target, "SELECT CONCAT(SellerId,'|',BuyerId,'|',Sentiment,'|',Comment) FROM dbo.SellerFeedback"));
        var again = await buyer.GetStringAsync(create);
        Assert.Contains("You left Positive feedback for the seller.", again);
        Assert.DoesNotContain("name=\"Sentiment\"", again);
        Assert.Equal((HttpStatusCode)409, (await Post(new() { ["Sentiment"] = "Negative" })).StatusCode);
        var detail = await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}");
        Assert.Contains("(1)", detail);
        Assert.Contains("100% positive feedback", detail);
        var review = await Post(new() { ["Rating"] = "5" });
        Assert.Equal(HttpStatusCode.Redirect, review.StatusCode);
        Assert.Contains("Thanks — your review was posted.", WebUtility.HtmlDecode(await buyer.GetStringAsync(review.Headers.Location!.OriginalString)));
    }
}
