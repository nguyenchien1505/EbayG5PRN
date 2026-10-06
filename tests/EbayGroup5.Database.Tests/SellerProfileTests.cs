using System.Net;
using DbUp.Engine;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Infrastructure.Reputation;
using Microsoft.Data.SqlClient;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

public sealed class SellerProfileTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static FeedbackService Service(SetupData s, DateTime? now = null) => new(Open(s.Target), new FixedClock(now ?? Now));

    // Direct fixture rows on fresh order lines; eligibility rules are covered by FeedbackTests.
    private static int Add(SetupData s, string sentiment, DateTime created, string? comment = null, int? seller = null)
    {
        var text = comment == null ? "NULL" : "N'" + comment.Replace("'", "''") + "'";
        return int.Parse(SqlFixture.Scalar(s.Target, $"""
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,12.34);
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment,CreatedAtUtc) VALUES(SCOPE_IDENTITY(),{s.Buyer},{seller ?? s.Seller},'{sentiment}',{text},'{created:yyyy-MM-ddTHH:mm:ss.fffffff}');
            SELECT SCOPE_IDENTITY();
            """));
    }

    [Fact]
    public async Task Score_is_lifetime_and_percentage_uses_the_last_twelve_months()
    {
        var s = await Setup();
        Add(s, "Positive", Now.AddDays(-10)); Add(s, "Neutral", Now.AddDays(-20)); Add(s, "Negative", Now.AddDays(-100));
        Add(s, "Positive", Now.AddDays(-200)); Add(s, "Positive", Now.AddMonths(-12)); Add(s, "Positive", Now.AddDays(-400));
        Add(s, "Negative", Now.AddDays(-400)); Add(s, "Negative", Now.AddDays(-700));
        Add(s, "Positive", Now.AddDays(-1), seller: s.OtherSeller);
        var rep = await Service(s).ReputationAsync(s.Seller);
        Assert.Equal(new FeedbackCounts(1, 1, 0), rep.Month1);
        Assert.Equal(new FeedbackCounts(1, 1, 1), rep.Month6);
        Assert.Equal(new FeedbackCounts(2, 1, 1), rep.Month12); // Exactly 12 months ago is outside the window.
        Assert.Equal(new FeedbackCounts(4, 1, 3), rep.Lifetime);
        Assert.Equal(1, rep.Score);
        Assert.Equal(66.7m, rep.PositivePercent);
        Assert.Equal(8, rep.Total);
        var later = await Service(s, Now.AddYears(3)).ReputationAsync(s.Seller);
        Assert.Equal(FeedbackCounts.Empty, later.Month12);
        Assert.Null(later.PositivePercent);
        Assert.Equal(1, later.Score);
        Assert.Equal(SellerReputation.Empty, await Service(s).ReputationAsync(s.Buyer));
    }

    [Fact]
    public async Task Profile_lists_masked_verified_entries_with_filter_and_bounded_pages()
    {
        var s = await Setup();
        Assert.Null(await Service(s).ProfileAsync(s.Buyer));
        Assert.Null(await Service(s).ProfileAsync(int.MaxValue));
        var empty = await Service(s).ProfileAsync(s.Seller);
        Assert.Equal(0, empty!.Total);
        Assert.Empty(empty.Entries);
        var ids = new List<int>();
        for (var i = 0; i < 27; i++) ids.Add(Add(s, i % 9 == 0 ? "Negative" : "Positive", Now.AddDays(-i * 20), "Comment " + i));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET ReplyText=N'Sorry, refunded', RepliedAtUtc='2026-10-01' WHERE Id={ids[0]}");
        var service = Service(s);
        var pages = new[] { await service.ProfileAsync(s.Seller, page: 1), await service.ProfileAsync(s.Seller, page: 2), await service.ProfileAsync(s.Seller, page: 3) };
        Assert.Equal(new[] { 25, 2, 0 }, pages.Select(x => x!.Entries.Count).ToArray());
        Assert.All(pages, x => Assert.Equal(2, x!.TotalPages));
        Assert.Equal(ids.OrderByDescending(x => x), pages.SelectMany(x => x!.Entries).Select(x => x.Id));
        var oldest = pages[1]!.Entries[^1];
        Assert.Equal(ids[0], oldest.Id);
        Assert.Equal("Sorry, refunded", oldest.Reply);
        Assert.Equal(FeedbackSentiment.Negative, oldest.Sentiment);
        Assert.Equal(FeedbackRecency.PastMonth, oldest.Recency);
        var first = pages[0]!.Entries[0];
        Assert.Equal("b***r", first.Buyer);
        Assert.Equal("Wireless Headphones", first.ItemTitle);
        Assert.Equal(12.34m, first.UnitPrice);
        Assert.Equal(FeedbackRecency.OlderThanYear, first.Recency);
        Assert.Equal(FeedbackRecency.Past6Months, pages[0]!.Entries.Single(x => x.Comment == "Comment 5").Recency);
        Assert.Equal(FeedbackRecency.PastYear, pages[0]!.Entries.Single(x => x.Comment == "Comment 10").Recency);
        var negative = await service.ProfileAsync(s.Seller, FeedbackSentiment.Negative);
        Assert.Equal(3, negative!.Total);
        Assert.All(negative.Entries, x => Assert.Equal(FeedbackSentiment.Negative, x.Sentiment));
        Assert.Equal("seller", (await service.ProfileAsync(s.Seller))!.Seller);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET username=N'ab' WHERE id={s.Buyer}");
        Assert.Equal("a***", (await Service(s).ProfileAsync(s.Seller))!.Entries[0].Buyer);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET username=NULL WHERE id={s.Buyer}");
        Assert.Equal("Buyer", (await Service(s).ProfileAsync(s.Seller))!.Entries[0].Buyer);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProfileAsync(s.Seller, page: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProfileAsync(s.Seller, (FeedbackSentiment)9));
    }

    [Fact]
    public async Task Only_the_receiving_seller_replies_once_and_replies_are_permanent()
    {
        var s = await Setup();
        var id = Add(s, "Negative", Now.AddDays(-3), "Late parcel");
        var other = Add(s, "Positive", Now.AddDays(-3), seller: s.OtherSeller);
        foreach (var bad in new[] { null, "", "   ", new string('x', FeedbackLimits.MaxReplyLength + 1), "bell\a" })
            Assert.Equal(ReplyOutcome.Invalid, (await Service(s).ReplyAsync(s.Seller, id, bad)).Outcome);
        Assert.Equal(ReplyOutcome.Forbidden, (await Service(s).ReplyAsync(s.Buyer, id, "buyer reply")).Outcome);
        Assert.Equal(ReplyOutcome.Forbidden, (await Service(s).ReplyAsync(s.Admin, id, "admin reply")).Outcome);
        Assert.Equal(ReplyOutcome.NotFound, (await Service(s).ReplyAsync(s.OtherSeller, id, "not mine")).Outcome);
        Assert.Equal(ReplyOutcome.NotFound, (await Service(s).ReplyAsync(s.Seller, other, "not mine")).Outcome);
        Assert.Equal(ReplyOutcome.NotFound, (await Service(s).ReplyAsync(s.Seller, int.MaxValue, "missing")).Outcome);
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.SellerFeedback WHERE ReplyText IS NOT NULL"));
        var results = await Task.WhenAll(Service(s).ReplyAsync(s.Seller, id, "  We are sorry \n  "), Service(s).ReplyAsync(s.Seller, id, "second"));
        Assert.Single(results, x => x.Outcome == ReplyOutcome.Success);
        Assert.Single(results, x => x.Outcome == ReplyOutcome.Conflict);
        var stored = SqlFixture.Scalar(s.Target, $"SELECT CONCAT(ReplyText,'|',CONVERT(varchar(19),RepliedAtUtc,126)) FROM dbo.SellerFeedback WHERE Id={id}");
        Assert.True(stored is "We are sorry|2026-10-06T12:00:00" or "second|2026-10-06T12:00:00", stored);
        Assert.Equal(ReplyOutcome.Conflict, (await Service(s).ReplyAsync(s.Seller, id, "edit")).Outcome);
        Assert.Equal(stored, SqlFixture.Scalar(s.Target, $"SELECT CONCAT(ReplyText,'|',CONVERT(varchar(19),RepliedAtUtc,126)) FROM dbo.SellerFeedback WHERE Id={id}"));
    }

    [Fact]
    public async Task Trigger_check_and_column_grants_keep_feedback_and_replies_immutable()
    {
        var s = await Setup();
        var id = Add(s, "Negative", Now, "Original");
        foreach (var sql in new[] { "Sentiment='Positive'", "Comment=N'Edited'", "Comment=NULL", $"SellerId={s.OtherSeller}", "CreatedAtUtc=SYSUTCDATETIME()" })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET {sql} WHERE Id={id}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET ReplyText=N'   ', RepliedAtUtc=SYSUTCDATETIME() WHERE Id={id}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET ReplyText=N'No date' WHERE Id={id}"));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET ReplyText=N'First', RepliedAtUtc=SYSUTCDATETIME() WHERE Id={id}");
        foreach (var sql in new[] { "ReplyText=N'Changed'", "ReplyText=NULL, RepliedAtUtc=NULL", "RepliedAtUtc='2020-01-01'" })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.SellerFeedback SET {sql} WHERE Id={id}"));
        Assert.Equal("Negative|Original|First", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(Sentiment,'|',Comment,'|',ReplyText) FROM dbo.SellerFeedback WHERE Id={id}"));

        var open = Add(s, "Positive", Now);
        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, $"UPDATE dbo.SellerFeedback SET ReplyText=N'Thanks', RepliedAtUtc=SYSUTCDATETIME() WHERE Id={open}");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, $"UPDATE dbo.SellerFeedback SET Sentiment='Negative' WHERE Id={open}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, $"DELETE dbo.SellerFeedback WHERE Id={open}"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.triggers WHERE name=N'TR_SellerFeedback_Immutable' AND is_disabled=0"));
    }

    [Fact]
    public void Migration_adds_reply_columns_without_changing_existing_feedback()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        var scripts = new List<SqlScript> { new("0001_Baseline.sql", Baseline.ReadTableDdl()) };
        var assembly = typeof(MigrationRunner).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".Scripts.") && !x.EndsWith("0009_SellerFeedbackReply.sql")).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..], reader.ReadToEnd()));
        }
        Assert.True(MigrationRunner.BuildEngine(target, scripts).PerformUpgrade().Successful);
        DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, """
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment,CreatedAtUtc)
            SELECT TOP(2) i.id, o.buyerId, o.SellerId, CASE WHEN i.id%2=0 THEN 'Positive' ELSE 'Negative' END, N'Existing', '2026-09-30'
            FROM dbo.OrderItem i JOIN dbo.OrderTable o ON o.id=i.orderId WHERE o.SellerId IS NOT NULL ORDER BY i.id
            """);
        var metadata = SqlFixture.Rows(target, SqlFixture.Metadata);
        const string rows = "SELECT CONCAT(Id,'|',OrderItemId,'|',BuyerId,'|',SellerId,'|',Sentiment,'|',Comment,'|',CONVERT(varchar(27),CreatedAtUtc,126)) FROM dbo.SellerFeedback";
        var before = SqlFixture.Rows(target, rows);
        Assert.Equal(2, before.Count);
        Assert.Equal(1, MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.True(before.SetEquals(SqlFixture.Rows(target, rows)));
        Assert.Equal("2", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.SellerFeedback WHERE ReplyText IS NULL AND RepliedAtUtc IS NULL"));
        Assert.Equal(0, MigrationRunner.Run(target));
    }

    [Fact]
    public async Task Http_profile_is_public_masked_and_only_the_owner_can_reply()
    {
        var s = await Setup();
        var id = Add(s, "Negative", DateTime.UtcNow.AddDays(-2), "Box <b>damaged</b>");
        Add(s, "Positive", DateTime.UtcNow.AddDays(-1), "Great");
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var seller = await Login(factory, "seller");
        using var otherSeller = await Login(factory, "otherseller");
        using var buyer = await Login(factory, "buyer");
        var path = $"/Sellers/Feedback/{s.Seller}";

        var page = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore == true);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Feedback profile", html);
        Assert.Contains("Positive Feedback (last 12 months): 50%", html);
        Assert.Contains("b***r", html);
        Assert.Contains("Verified purchase", html);
        Assert.Contains("Box &lt;b&gt;damaged&lt;/b&gt;", html);
        Assert.DoesNotContain("@d01.example.invalid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"feedbackId\"", html);
        Assert.DoesNotContain("name=\"feedbackId\"", await otherSeller.GetStringAsync(path));
        Assert.Contains("name=\"feedbackId\"", await seller.GetStringAsync(path));
        Assert.Contains("Feedback profile", await seller.GetStringAsync("/Account/Me"));
        foreach (var query in new[] { "?sentiment=Positive", "?sentiment=bad", "?page=0", "?page=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync(path + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/Sellers/Feedback/{s.Buyer}")).StatusCode);
        Assert.Contains("Great", await anonymous.GetStringAsync(path + "?sentiment=positive"));
        Assert.DoesNotContain("damaged", await anonymous.GetStringAsync(path + "?sentiment=positive"));
        Assert.Contains($"/Sellers/Feedback/{s.Seller}", await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}"));
        Assert.Contains("50% positive feedback", await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}"));

        async Task<HttpResponseMessage> Reply(HttpClient client, string text, bool token = true)
        {
            var form = new Dictionary<string, string> { ["feedbackId"] = id.ToString(), ["reply"] = text };
            if (token) form["__RequestVerificationToken"] = Field(await client.GetStringAsync("/Account/Me"));
            return await client.PostAsync("/Sellers/Reply", new FormUrlEncodedContent(form));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Reply(seller, "no token", token: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reply(buyer, "buyer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Reply(otherSeller, "not mine")).StatusCode);
        var empty = await Reply(seller, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("Enter a reply.", await empty.Content.ReadAsStringAsync());
        var posted = await Reply(seller, "Sorry — we sent a <i>replacement</i>.");
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        var after = await seller.GetStringAsync(posted.Headers.Location!.OriginalString);
        Assert.Contains("Your reply was posted.", after);
        Assert.Contains("we sent a &lt;i&gt;replacement&lt;/i&gt;.", after);
        Assert.Contains("Reply by seller", after);
        var again = await Reply(seller, "edit");
        Assert.Equal((HttpStatusCode)409, again.StatusCode);
        Assert.Contains("You already replied to this feedback.", await again.Content.ReadAsStringAsync());
        Assert.Contains("replacement", await anonymous.GetStringAsync(path));
    }
}
