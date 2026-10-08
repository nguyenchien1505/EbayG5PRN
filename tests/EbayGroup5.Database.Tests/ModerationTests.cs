using System.Net;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Moderation;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Moderation;
using EbayGroup5.Infrastructure.Reputation;
using EbayGroup5.Infrastructure.Reviews;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

public sealed class ModerationTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static ModerationService Moderation(SetupData s) => new(Open(s.Target), new FixedClock(Now));
    private static FeedbackService Feedback(SetupData s) => new(Open(s.Target), new FixedClock(Now));
    private static ReviewService Reviews(SetupData s) => new(Open(s.Target));

    private static async Task<int> Moderator(SetupData s)
    {
        await using var db = Open(s.Target);
        Assert.True(await new AccountService(db).RegisterAsync("moderator", "moderator@d01.example.invalid", Password));
        await db.Set<User>().Where(x => x.Email == "MODERATOR@D01.EXAMPLE.INVALID").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
        return await db.Set<User>().Where(x => x.Email == "MODERATOR@D01.EXAMPLE.INVALID").Select(x => x.Id).SingleAsync();
    }

    private static int Review(SetupData s, int rating, string? comment)
    {
        var text = comment == null ? "NULL" : "N'" + comment.Replace("'", "''") + "'";
        return int.Parse(SqlFixture.Scalar(s.Target, $"""
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90);
            INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt) VALUES({s.Product},{s.Buyer},SCOPE_IDENTITY(),{rating},{text},SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
            """));
    }

    private static int Feedback(SetupData s, string sentiment, string? comment, string? reply = null)
    {
        var text = comment == null ? "NULL" : "N'" + comment + "'";
        var replySql = reply == null ? "NULL,NULL" : $"N'{reply}',SYSUTCDATETIME()";
        return int.Parse(SqlFixture.Scalar(s.Target, $"""
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,49.90);
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment,CreatedAtUtc,ReplyText,RepliedAtUtc) VALUES(SCOPE_IDENTITY(),{s.Buyer},{s.Seller},'{sentiment}',{text},'2026-10-01',{replySql});
            SELECT SCOPE_IDENTITY();
            """));
    }

    private static string Audits(SetupData s) => SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.ModerationAudit");

    [Fact]
    public async Task Review_moderation_updates_average_counts_and_list_and_keeps_audit()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var admin = await Moderator(s);
        var good = Review(s, 5, "Great"); var spam = Review(s, 1, "Visit my shop"); Review(s, 3, null);
        async Task<ProductReviewPage> Page() => await Reviews(s).ProductReviewsAsync(s.Product);
        Assert.Equal((3, 2, 3.0m), ((await Page()).Summary.Ratings, (await Page()).Summary.Reviews, (await Page()).Summary.Average!.Value));
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, spam, ReportReason.Spam, "  link to another site ")).Outcome);
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.RemoveComment, "Promotional link")).Outcome);
        var removed = await Page();
        Assert.Equal((3, 1, 3.0m), (removed.Summary.Ratings, removed.Summary.Reviews, removed.Summary.Average!.Value));
        Assert.DoesNotContain(removed.Reviews, x => x.Id == spam);
        Assert.Equal($"Actioned|{admin}", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(Status,'|',ResolvedById) FROM dbo.ContentReport WHERE ReviewId={spam}"));

        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.Hide, null)).Outcome);
        var hidden = await Page();
        Assert.Equal((2, 1, 4.0m), (hidden.Summary.Ratings, hidden.Summary.Reviews, hidden.Summary.Average!.Value));
        Assert.Equal(new[] { 1, 0, 1, 0, 0 }, hidden.Summary.Distribution);
        Assert.Equal(ModerationOutcome.Conflict, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.Hide, null)).Outcome);
        Assert.Equal(ModerationOutcome.Conflict, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.RemoveComment, null)).Outcome);
        Assert.Equal(ModerationOutcome.Conflict, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.Dismiss, null)).Outcome);
        Assert.Null(await Moderation(s).FindReportableAsync(ContentKind.Review, spam));
        var spamItem = int.Parse(SqlFixture.Scalar(s.Target, $"SELECT OrderItemId FROM dbo.Review WHERE id={spam}"));
        Assert.Equal(ReviewOutcome.Conflict, (await Reviews(s).SubmitAsync(s.Buyer, spamItem, 5, "try again")).Outcome); // Hidden still counts as reviewed.

        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.Restore, "Appeal accepted")).Outcome);
        var restored = await Page();
        Assert.Equal((3, 2), (restored.Summary.Ratings, restored.Summary.Reviews));
        Assert.Contains(restored.Reviews, x => x.Id == spam && x.Comment == "Visit my shop");
        Assert.Equal(ModerationOutcome.Conflict, (await Moderation(s).ActAsync(admin, ContentKind.Review, spam, ModerationAction.Restore, null)).Outcome);
        Assert.Equal("3", Audits(s));
        var detail = await Moderation(s).DetailAsync(admin, ContentKind.Review, spam);
        Assert.Equal(new[] { ModerationAction.Restore, ModerationAction.Hide, ModerationAction.RemoveComment }, detail!.Audit.Select(x => x.Action).ToArray());
        Assert.Equal("Appeal accepted", detail.Audit[0].Note);
        Assert.Equal("link to another site", Assert.Single(detail.Reports).Details);
        Assert.Equal(5, (await Reviews(s).ProductReviewsAsync(s.Product)).Reviews.Single(x => x.Id == good).Rating);
    }

    [Fact]
    public async Task Feedback_moderation_updates_reputation_profile_and_closes_replies()
    {
        var s = await Setup();
        var admin = await Moderator(s);
        var ok = Feedback(s, "Positive", "Fine"); var rude = Feedback(s, "Negative", "Rude words", "Sorry"); var spam = Feedback(s, "Negative", "Spam");
        // H01: same buyer and transaction week -> one Positive and one Negative count (raw 1 P / 2 N).
        Assert.Equal((0, 50m), ((await Feedback(s).ReputationAsync(s.Seller)).Score, (await Feedback(s).ReputationAsync(s.Seller)).PositivePercent!.Value));
        Assert.Equal(new FeedbackCounts(1, 0, 2), (await Feedback(s).ReputationAsync(s.Seller)).Lifetime);
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.SellerFeedback, rude, ModerationAction.RemoveComment, null)).Outcome);
        var profile = await Feedback(s).ProfileAsync(s.Seller);
        var entry = profile!.Entries.Single(x => x.Id == rude);
        Assert.Null(entry.Comment);
        Assert.Null(entry.Reply);
        Assert.Equal(FeedbackSentiment.Negative, entry.Sentiment);
        Assert.Equal(0, profile.Reputation.Score);
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.SellerFeedback, spam, ModerationAction.Hide, "Spam")).Outcome);
        var rep = await Feedback(s).ReputationAsync(s.Seller);
        Assert.Equal((0, 50m), (rep.Score, rep.PositivePercent!.Value));
        Assert.Equal(new FeedbackCounts(1, 0, 1), rep.Lifetime);
        Assert.Equal(2, (await Feedback(s).ProfileAsync(s.Seller))!.Total);
        Assert.Equal(ReplyOutcome.NotFound, (await Feedback(s).ReplyAsync(s.Seller, spam, "reply to hidden")).Outcome);
        Assert.Contains("Replies are closed", (await Feedback(s).ReplyAsync(s.Seller, rude, "x")).Error);
        Assert.Equal(ReplyOutcome.Success, (await Feedback(s).ReplyAsync(s.Seller, ok, "Thanks")).Outcome);
        Assert.Equal("Sorry", SqlFixture.Scalar(s.Target, $"SELECT ReplyText FROM dbo.SellerFeedback WHERE Id={rude}")); // Hidden, not erased.
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.SellerFeedback, rude, ModerationAction.Restore, null)).Outcome);
        Assert.Equal("Sorry", (await Feedback(s).ProfileAsync(s.Seller))!.Entries.Single(x => x.Id == rude).Reply);
    }

    [Fact]
    public async Task Report_rules_cover_roles_authors_duplicates_input_and_hidden_content()
    {
        var s = await Setup();
        var admin = await Moderator(s);
        var review = Review(s, 2, "Bad");
        var feedback = Feedback(s, "Negative", "Seller lied");
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,rating,comment) VALUES({s.Product},{s.OtherBuyer},5,N'legacy')");
        var legacy = int.Parse(SqlFixture.Scalar(s.Target, "SELECT MAX(id) FROM dbo.Review WHERE OrderItemId IS NULL"));
        Assert.Equal(ModerationOutcome.Forbidden, (await Moderation(s).ReportAsync(s.Buyer, ContentKind.Review, review, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.Forbidden, (await Moderation(s).ReportAsync(s.Buyer, ContentKind.SellerFeedback, feedback, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.Forbidden, (await Moderation(s).ReportAsync(admin, ContentKind.Review, review, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.Forbidden, (await Moderation(s).ReportAsync(int.MaxValue, ContentKind.Review, review, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.Invalid, (await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, review, (ReportReason)9, null)).Outcome);
        Assert.Equal(ModerationOutcome.Invalid, (await Moderation(s).ReportAsync(s.OtherBuyer, (ContentKind)9, review, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.Invalid, (await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, review, ReportReason.Other, new string('x', 501))).Outcome);
        Assert.Equal(ModerationOutcome.NotFound, (await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, legacy, ReportReason.Other, null)).Outcome);
        Assert.Equal(ModerationOutcome.NotFound, (await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, int.MaxValue, ReportReason.Other, null)).Outcome);
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.ContentReport"));

        var results = await Task.WhenAll(Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, review, ReportReason.Offensive, null),
            Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, review, ReportReason.Spam, null));
        Assert.Single(results, x => x.Outcome == ModerationOutcome.Success);
        Assert.Single(results, x => x.Outcome == ModerationOutcome.Conflict);
        var sellerReport = await Moderation(s).ReportAsync(s.Seller, ContentKind.SellerFeedback, feedback, ReportReason.Other, "  Not true  ");
        Assert.Equal(ModerationOutcome.Success, sellerReport.Outcome);
        Assert.Equal($"/Sellers/Feedback/{s.Seller}", sellerReport.ReturnPath);
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ReportAsync(s.OtherSeller, ContentKind.Review, review, ReportReason.PersonalInfo, null)).Outcome);
        Assert.Equal("Not true", SqlFixture.Scalar(s.Target, $"SELECT Details FROM dbo.ContentReport WHERE SellerFeedbackId={feedback}"));
        await Moderation(s).ActAsync(admin, ContentKind.Review, review, ModerationAction.Hide, null);
        Assert.Equal(ModerationOutcome.NotFound, (await Moderation(s).ReportAsync(s.Seller, ContentKind.Review, review, ReportReason.Other, null)).Outcome);
        Assert.Equal("2", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.ContentReport WHERE ReviewId={review} AND Status='Actioned'"));
    }

    [Fact]
    public async Task Admin_queue_detail_and_actions_require_admin_and_serialize()
    {
        var s = await Setup();
        var admin = await Moderator(s);
        var first = Review(s, 1, "first"); var second = Feedback(s, "Negative", "second");
        await Moderation(s).ReportAsync(s.OtherBuyer, ContentKind.Review, first, ReportReason.Offensive, null);
        await Moderation(s).ReportAsync(s.OtherSeller, ContentKind.Review, first, ReportReason.Spam, null);
        SqlFixture.Execute(s.Target, "UPDATE dbo.ContentReport SET CreatedAtUtc='2026-10-01'");
        await Moderation(s).ReportAsync(s.Seller, ContentKind.SellerFeedback, second, ReportReason.Other, null);
        foreach (var actor in new[] { s.Buyer, s.Seller, s.Admin, int.MaxValue })
        {
            Assert.Null(await Moderation(s).QueueAsync(actor));
            Assert.Null(await Moderation(s).DetailAsync(actor, ContentKind.Review, first));
            Assert.Equal(ModerationOutcome.Forbidden, (await Moderation(s).ActAsync(actor, ContentKind.Review, first, ModerationAction.Hide, null)).Outcome);
        }
        var queue = (await Moderation(s).QueueAsync(admin))!;
        Assert.Equal(new[] { (ContentKind.SellerFeedback, second), (ContentKind.Review, first) }, queue.Select(x => (x.Kind, x.Id)).ToArray());
        Assert.Equal(2, queue[1].OpenReports);
        Assert.Equal(new[] { ReportReason.Offensive, ReportReason.Spam }, queue[1].Reasons);
        Assert.Equal(ModerationOutcome.NotFound, (await Moderation(s).ActAsync(admin, ContentKind.Review, int.MaxValue, ModerationAction.Hide, null)).Outcome);
        Assert.Equal(ModerationOutcome.Invalid, (await Moderation(s).ActAsync(admin, ContentKind.Review, first, (ModerationAction)9, null)).Outcome);
        Assert.Equal(ModerationOutcome.Invalid, (await Moderation(s).ActAsync(admin, ContentKind.Review, first, ModerationAction.Hide, "bell\a")).Outcome);
        Assert.Equal("0", Audits(s));

        var results = await Task.WhenAll(Moderation(s).ActAsync(admin, ContentKind.Review, first, ModerationAction.Hide, "a"),
            Moderation(s).ActAsync(admin, ContentKind.Review, first, ModerationAction.Hide, "b"));
        Assert.Single(results, x => x.Outcome == ModerationOutcome.Success);
        Assert.Single(results, x => x.Outcome == ModerationOutcome.Conflict);
        Assert.Equal("1", Audits(s));
        Assert.Equal(ModerationOutcome.Success, (await Moderation(s).ActAsync(admin, ContentKind.SellerFeedback, second, ModerationAction.Dismiss, "Valid feedback")).Outcome);
        Assert.Empty((await Moderation(s).QueueAsync(admin))!);
        Assert.Equal("Dismissed", SqlFixture.Scalar(s.Target, $"SELECT Status FROM dbo.ContentReport WHERE SellerFeedbackId={second}"));
        Assert.Equal(-1, (await Feedback(s).ReputationAsync(s.Seller)).Score); // Dismiss leaves content counted.
        var detail = (await Moderation(s).DetailAsync(admin, ContentKind.Review, first))!;
        Assert.NotNull(detail.HiddenAtUtc);
        Assert.Equal("first", detail.Comment);
        Assert.All(detail.Reports, x => Assert.Equal("Actioned", x.Status));

        SqlFixture.Execute(s.Target, "CREATE TRIGGER dbo.AuditFailure ON dbo.ModerationAudit AFTER INSERT AS THROW 51012, 'Isolated test failure', 1;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Moderation(s).ActAsync(admin, ContentKind.Review, first, ModerationAction.Restore, null));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Review WHERE id={first} AND HiddenAtUtc IS NOT NULL"));
        Assert.Equal("2", Audits(s)); // Hide + Dismiss; the failed Restore left nothing.
    }

    [Fact]
    public async Task Schema_blocks_deletes_audit_edits_and_invalid_reports()
    {
        var s = await Setup();
        var admin = await Moderator(s);
        var review = Review(s, 4, "keep");
        var feedback = Feedback(s, "Positive", "fine");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"DELETE dbo.Review WHERE id={review}"));
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,rating) VALUES({s.Product},2); DELETE dbo.Review WHERE OrderItemId IS NULL");
        foreach (var sql in new[]
        {
            $"INSERT dbo.ContentReport(ReviewId,SellerFeedbackId,ReporterId,Reason) VALUES({review},{feedback},{s.OtherBuyer},'Spam')",
            $"INSERT dbo.ContentReport(ReporterId,Reason) VALUES({s.OtherBuyer},'Spam')",
            $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason) VALUES({review},{s.OtherBuyer},'spam')",
            $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason,Details) VALUES({review},{s.OtherBuyer},'Spam',N'  ')",
            $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason,Status) VALUES({review},{s.OtherBuyer},'Spam','Actioned')",
            $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason,ResolvedAtUtc,ResolvedById) VALUES({review},{s.OtherBuyer},'Spam',SYSUTCDATETIME(),{admin})",
            $"INSERT dbo.ModerationAudit(ReviewId,AdminId,Action) VALUES({review},{admin},'Delete')",
        })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, sql));
        SqlFixture.Execute(s.Target, $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason) VALUES({review},{s.OtherBuyer},'Spam')");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"INSERT dbo.ContentReport(ReviewId,ReporterId,Reason) VALUES({review},{s.OtherBuyer},'Other')"));
        await Moderation(s).ActAsync(admin, ContentKind.SellerFeedback, feedback, ModerationAction.Hide, "note");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, "UPDATE dbo.ModerationAudit SET Note=N'edited'"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, "DELETE dbo.ModerationAudit"));
        Assert.Equal("1", Audits(s));

        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, $"UPDATE dbo.SellerFeedback SET HiddenAtUtc=NULL WHERE Id={feedback}");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, $"UPDATE dbo.SellerFeedback SET Sentiment='Negative' WHERE Id={feedback}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "UPDATE dbo.ContentReport SET Reason='Other'"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "DELETE dbo.ContentReport"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "DELETE dbo.ModerationAudit"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, $"DELETE dbo.Review WHERE id={review}"));
    }

    [Fact]
    public void Migration_adds_moderation_without_changing_existing_reviews_or_feedback()
    {
        var target = SqlFixture.NewTarget();
        var remaining = SqlFixture.MigrateBefore(target, "0010");
        DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, """
            INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt)
            SELECT TOP(1) i.productId,o.buyerId,i.id,4,N'existing',SYSUTCDATETIME() FROM dbo.OrderItem i JOIN dbo.OrderTable o ON o.id=i.orderId ORDER BY i.id;
            INSERT dbo.Review(productId,rating,comment) SELECT TOP(1) id,9,N'legacy' FROM dbo.Product;
            INSERT dbo.SellerFeedback(OrderItemId,BuyerId,SellerId,Sentiment,Comment,ReplyText,RepliedAtUtc)
            SELECT TOP(1) i.id,o.buyerId,o.SellerId,'Negative',N'existing',N'reply',SYSUTCDATETIME() FROM dbo.OrderItem i JOIN dbo.OrderTable o ON o.id=i.orderId WHERE o.SellerId IS NOT NULL ORDER BY i.id DESC;
            """);
        var metadata = SqlFixture.Rows(target, SqlFixture.Metadata);
        const string rows = "SELECT CONCAT('R|',id,'|',rating,'|',comment,'|',OrderItemId) FROM dbo.Review UNION ALL SELECT CONCAT('F|',Id,'|',Sentiment,'|',Comment,'|',ReplyText) FROM dbo.SellerFeedback";
        var before = SqlFixture.Rows(target, rows);
        Assert.Equal(3, before.Count);
        Assert.Equal(remaining, MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.True(before.SetEquals(SqlFixture.Rows(target, rows)));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT (SELECT COUNT(*) FROM dbo.Review WHERE HiddenAtUtc IS NOT NULL OR CommentRemovedAtUtc IS NOT NULL) + (SELECT COUNT(*) FROM dbo.SellerFeedback WHERE HiddenAtUtc IS NOT NULL) + (SELECT COUNT(*) FROM dbo.ContentReport) + (SELECT COUNT(*) FROM dbo.ModerationAudit)"));
        Assert.Equal(0, MigrationRunner.Run(target));
    }

    [Fact]
    public async Task Http_report_and_admin_moderation_journey_with_network_and_role_guards()
    {
        var s = await Setup();
        await Moderator(s);
        var review = Review(s, 1, "Call me at 0123 <b>now</b>");
        var feedback = Feedback(s, "Negative", "Seller was rude");
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var author = await Login(factory, "buyer");
        using var reporter = await Login(factory, "otherbuyer");
        using var seller = await Login(factory, "seller");
        using var admin = await Login(factory, "moderator");
        var reviews = $"/Catalog/Reviews/{s.Product}";
        var reportPath = $"/Reports/Create/{review}?kind=review";

        Assert.DoesNotContain(reportPath, await anonymous.GetStringAsync(reviews));
        Assert.DoesNotContain(reportPath, await author.GetStringAsync(reviews));
        Assert.Contains(reportPath, await reporter.GetStringAsync(reviews));
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(reportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(reportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reporter.GetAsync("/Reports/Create?kind=post&id=1")).StatusCode);
        var form = await reporter.GetStringAsync(reportPath);
        Assert.Contains("Why are you reporting this?", form);
        async Task<HttpResponseMessage> Report(HttpClient client, string kind, int id, string reason, bool token = true)
        {
            var fields = new Dictionary<string, string> { ["kind"] = kind, ["id"] = id.ToString(), ["reason"] = reason, ["details"] = "Phone number" };
            if (token) fields["__RequestVerificationToken"] = Field(await client.GetStringAsync("/Account/Me"));
            return await client.PostAsync("/Reports/Create", new FormUrlEncodedContent(fields));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(reporter, "review", review, "PersonalInfo", token: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(reporter, "review", review, "9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Report(author, "review", review, "Other")).StatusCode);
        var posted = await Report(reporter, "review", review, "PersonalInfo");
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal(reviews, posted.Headers.Location!.OriginalString);
        Assert.Contains("we&#x27;ll review your report", await reporter.GetStringAsync(reviews));
        Assert.Equal((HttpStatusCode)409, (await Report(reporter, "review", review, "Spam")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Report(seller, "feedback", feedback, "Other")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reporter.GetAsync("/Admin/Moderation")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Admin/Moderation")).StatusCode);
        var queue = await admin.GetStringAsync("/Admin/Moderation");
        Assert.Contains($"Review #{review}", queue);
        Assert.Contains($"Seller feedback #{feedback}", queue);
        Assert.Contains("Moderation", await admin.GetStringAsync("/Account/Me"));
        var item = await admin.GetStringAsync($"/Admin/ModerationItem?kind=review&id={review}");
        Assert.Contains("Call me at 0123 &lt;b&gt;now&lt;/b&gt;", item);
        Assert.Contains("PersonalInfo", item);
        async Task<HttpResponseMessage> Moderate(HttpClient client, string kind, int id, string action)
        {
            var fields = new Dictionary<string, string> { ["kind"] = kind, ["id"] = id.ToString(), ["action"] = action, ["note"] = "Contains a phone number",
                ["__RequestVerificationToken"] = Field(await client.GetStringAsync("/Account/Me")) };
            return await client.PostAsync("/Admin/Moderate", new FormUrlEncodedContent(fields));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Moderate(seller, "review", review, "Hide")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Moderate(admin, "review", review, "Delete")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Moderate(admin, "review", review, "Hide")).StatusCode);
        Assert.Equal((HttpStatusCode)409, (await Moderate(admin, "review", review, "Hide")).StatusCode);
        var publicReviews = await anonymous.GetStringAsync(reviews);
        Assert.DoesNotContain("Call me at", publicReviews);
        Assert.Contains("No ratings yet", await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}"));
        Assert.Equal(HttpStatusCode.Redirect, (await Moderate(admin, "feedback", feedback, "RemoveComment")).StatusCode);
        var profile = await anonymous.GetStringAsync($"/Sellers/Feedback/{s.Seller}");
        Assert.DoesNotContain("Seller was rude", profile);
        Assert.Contains("Negative feedback", profile);
        var audit = await admin.GetStringAsync($"/Admin/ModerationItem?kind=review&id={review}");
        Assert.Contains("Hide", audit);
        Assert.Contains("Contains a phone number", audit);

        using var remote = new AccountHttpTests.Factory(s.Target, "192.0.2.10");
        using var outside = await Login(remote, "moderator");
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync("/Admin/Moderation")).StatusCode);
    }
}
