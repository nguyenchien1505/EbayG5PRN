using System.Net;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>E05: admin review queue, decisions and audit (DISPUTE_ADMIN.md, G5-08, SUP-05).</summary>
public sealed class DisputeAdminTests
{
    private static readonly DisputeTiming Timing = new();

    private static async Task<T> With<T>(SetupData s, Func<DisputeService, Task<T>> run)
    {
        await using var db = Open(s.Target);
        return await run(new DisputeService(db, TimeProvider.System, Timing));
    }
    private static async Task<int> Admin(SetupData s, string name)
    {
        await using var db = Open(s.Target);
        Assert.True(await new AccountService(db).RegisterAsync(name, name + "@d01.example.invalid", Password));
        var email = (name + "@d01.example.invalid").ToUpperInvariant();
        await db.Set<User>().Where(x => x.Email == email).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
        return await db.Set<User>().Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
    }
    private static int AddLine(SetupData s)
    {
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},1,10.00)");
        return int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MAX(id) FROM dbo.OrderItem WHERE orderId={s.Order}"));
    }
    private static async Task<int> OpenInr(SetupData s, int line) =>
        (await With(s, x => x.OpenAsync(s.Buyer, new(line, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, "Never came")))).DisputeId!.Value;
    private static async Task<int> Escalated(SetupData s, int line)
    {
        var id = await OpenInr(s, line);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE id={id}");
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(s.Buyer, id, new(DisputeAction.Escalate, "Still nothing")))).Code);
        return id;
    }
    private static Task<DisputeQueue?> Queue(SetupData s, int actor, DisputeQueueState state = DisputeQueueState.Waiting, int page = 1) =>
        With(s, x => x.QueueAsync(actor, new(state, page)));
    private static async Task<SetupData> Ready()
    {
        var s = await Setup();
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-2,SYSUTCDATETIME()) WHERE id={s.Order}");
        return s;
    }

    [Fact]
    public async Task Queue_is_for_credentialed_admins_ordered_by_review_deadline_and_paged()
    {
        var s = await Ready();
        var admin = await Admin(s, "e05queue");
        var ids = new List<int> { await Escalated(s, s.Item) };
        for (var i = 0; i < 20; i++) ids.Add(await Escalated(s, AddLine(s)));
        var notEscalated = await OpenInr(s, AddLine(s));
        // Review deadlines out of id order: the queue follows the deadline.
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(hour,60,SYSUTCDATETIME()) WHERE id={ids[0]}");
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(hour,-2,SYSUTCDATETIME()) WHERE id={ids[5]}");

        var page1 = (await Queue(s, admin))!;
        Assert.Equal((21, 2, 20), (page1.Total, page1.TotalPages, page1.Items.Count));
        Assert.Equal(ids[5], page1.Items[0].Id);
        Assert.Equal(page1.Items.Select(x => x.DueAtUtc).Order(), page1.Items.Select(x => x.DueAtUtc));
        Assert.Equal(ids[0], Assert.Single((await Queue(s, admin, page: 2))!.Items).Id);
        Assert.DoesNotContain(notEscalated, page1.Items.Select(x => x.Id));
        var item = page1.Items.Single(x => x.Id == ids[1]);
        Assert.Equal(("buyer", "seller", 10.00m, DisputeStatus.Escalated), (item.BuyerName!, item.SellerName!, item.LineTotal, item.Status));
        Assert.NotNull(item.EscalatedAtUtc);
        Assert.Empty((await Queue(s, admin, DisputeQueueState.Closed))!.Items);

        // Everyone else: buyers, sellers and an Admin row without a credential.
        foreach (var actor in new[] { s.Buyer, s.Seller, s.OtherSeller, int.MaxValue }) Assert.Null(await Queue(s, actor));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET password=NULL WHERE id={admin}");
        Assert.Null(await Queue(s, admin));
        Assert.Null(await With(s, x => x.FindAsync(admin, ids[1], 1)));
        Assert.Equal(DisputeResultCode.NotFound, (await With(s, x => x.ActAsync(admin, ids[1], new(DisputeAction.Decide, "x", ForBuyer: true)))).Code);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Queue(s, s.Admin, page: 0));
    }

    [Fact]
    public async Task Decisions_close_once_refund_in_simulation_and_leave_a_named_audit_trail()
    {
        var s = await Ready();
        var first = await Admin(s, "e05first");
        var second = await Admin(s, "e05second");
        var forBuyer = await Escalated(s, s.Item);
        var forSeller = await Escalated(s, AddLine(s));
        var refunded = await Escalated(s, AddLine(s));
        var payment = SqlFixture.Scalar(s.Target, $"SELECT CONCAT(COUNT(*),'|',MAX(status),'|',MAX(amount)) FROM dbo.Payment WHERE orderId={s.Order}");

        // Two admins decide at once: one wins under the row lock, the other gets Conflict and writes nothing.
        var results = await Task.WhenAll(new[] { first, second }.Select(admin => Task.Run(() =>
            With(s, x => x.ActAsync(admin, forBuyer, new(DisputeAction.Decide, "Seller gave no tracking.", ForBuyer: true))))));
        Assert.Equal(new[] { DisputeResultCode.Success, DisputeResultCode.Conflict }, results.Select(x => x.Code).Order());
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE DisputeId={forBuyer} AND Type='Decided'"));
        Assert.Equal("Closed|DecidedForBuyer|49.90|Seller gave no tracking.", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT,'|',RefundAmount,'|',resolution) FROM dbo.Dispute WHERE id={forBuyer}"));
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(first, forSeller, new(DisputeAction.Decide, "Tracking shows delivery.", ForBuyer: false)))).Code);
        Assert.Equal("Closed|DecidedForSeller||Tracking shows delivery.", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT,'|',RefundAmount,'|',resolution) FROM dbo.Dispute WHERE id={forSeller}"));
        // The seller refunds while eBay reviews; a later decision is a Conflict.
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(s.Seller, refunded, new(DisputeAction.Refund)))).Code);
        Assert.Equal(DisputeResultCode.Conflict, (await With(s, x => x.ActAsync(first, refunded, new(DisputeAction.Decide, "Too late", ForBuyer: true)))).Code);
        Assert.Equal(payment, SqlFixture.Scalar(s.Target, $"SELECT CONCAT(COUNT(*),'|',MAX(status),'|',MAX(amount)) FROM dbo.Payment WHERE orderId={s.Order}"));

        var closed = (await Queue(s, first, DisputeQueueState.Closed))!;
        Assert.Equal(new[] { refunded, forSeller, forBuyer }, closed.Items.Select(x => x.Id));
        Assert.Equal((DisputeOutcome.Refunded, 10.00m), (closed.Items[0].Outcome!.Value, closed.Items[0].RefundAmount!.Value));
        Assert.Empty((await Queue(s, first))!.Items);

        // Audit: admins see who acted; buyers and sellers never see admin names.
        var audit = (await With(s, x => x.FindAsync(first, forBuyer, 1)))!;
        Assert.Equal(DisputeActor.Admin, audit.Viewer);
        Assert.Equal((DisputeActor.Admin, DisputeEventType.Decided, "Seller gave no tracking.", 49.90m), (audit.Events[0].ActorRole, audit.Events[0].Type, audit.Events[0].Message!, audit.Events[0].Amount!.Value));
        Assert.Contains(audit.Events[0].ActorName, new[] { "e05first", "e05second" });
        Assert.Equal(new[] { "buyer", "buyer" }, audit.Events.Skip(1).Select(x => x.ActorName));
        var buyerView = (await With(s, x => x.FindAsync(s.Buyer, forBuyer, 1)))!;
        Assert.All(buyerView.Events, e => Assert.Null(e.ActorName));
        Assert.Equal((DisputeStatus.Closed, DisputeOutcome.DecidedForBuyer), (buyerView.Status, buyerView.Outcome!.Value));
        // Requests are never deleted, closed ones never reopen.
        Assert.ThrowsAny<Exception>(() => SqlFixture.Execute(s.Target, $"DELETE dbo.Dispute WHERE id={forBuyer}"));
        Assert.ThrowsAny<Exception>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET status='Escalated', ClosedAtUtc=NULL, Outcome=NULL WHERE id={forBuyer}"));
    }

    [Fact]
    public async Task Http_admin_reviews_decides_and_both_parties_see_the_result()
    {
        var s = await Ready();
        await Admin(s, "e05admin");
        var id = await Escalated(s, s.Item);
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var seller = await Login(factory, "seller");
        using var admin = await Login(factory, "e05admin");
        var url = $"/Admin/Dispute/{id}";

        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Admin/Disputes")).StatusCode);
        foreach (var user in new[] { buyer, seller })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/Admin/Disputes")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(url)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Disputes?state=Bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Dispute/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/Admin/Dispute/{int.MaxValue}")).StatusCode);

        var queue = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Disputes"));
        foreach (var text in new[] { "Requests for eBay review", "Waiting for a decision", "Waiting for an eBay decision", "Decide by", "Buyer: buyer · Seller: seller", "Wireless Headphones",
                     $"href=\"/Admin/Dispute/{id}\">Review</a>", "href=\"/Admin/Disputes\">Request reviews</a>" })
            Assert.Contains(text, queue);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(url));
        foreach (var text in new[] { "Review request #" + id, "Audit trail", "Buyer buyer", "reported that the item didn't arrive", "asked eBay to step in", "Still nothing",
                     "Decide for the buyer", "Refund US $49.90 (simulated) and close the request.", "Decide for the seller", "Confirm decision", "Message both parties" })
            Assert.Contains(text, page);

        Dictionary<string, string> Form(string html, Dictionary<string, string> fields) { fields["__RequestVerificationToken"] = Field(html); return fields; }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["DisputeId"] = id.ToString(), ["Action"] = "Message", ["Message"] = "no token" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new()
            { ["DisputeId"] = id.ToString(), ["Action"] = "Refund" })))).StatusCode);
        var noSide = await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["Message"] = "Typed note" })));
        Assert.Equal(HttpStatusCode.BadRequest, noSide.StatusCode);
        var noSideHtml = WebUtility.HtmlDecode(await noSide.Content.ReadAsStringAsync());
        Assert.Contains("Choose who the decision favors.", noSideHtml); Assert.Contains("Typed note", noSideHtml);
        var noNote = await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["ForBuyer"] = "true" })));
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        Assert.Contains("Enter a message.", WebUtility.HtmlDecode(await noNote.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(await buyer.GetStringAsync("/Account/Me"), new()
            { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["ForBuyer"] = "true", ["Message"] = "self-serve" })))).StatusCode);
        Assert.Equal("Escalated", SqlFixture.Scalar(s.Target, $"SELECT status FROM dbo.Dispute WHERE id={id}"));

        var message = await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new()
            { ["DisputeId"] = id.ToString(), ["Action"] = "Message", ["Message"] = "Please share <b>tracking</b>." })));
        Assert.Equal((HttpStatusCode.Redirect, url), (message.StatusCode, message.Headers.Location!.OriginalString));
        Assert.Contains("Message sent to the buyer and seller.", await admin.GetStringAsync(url));
        var sellerPage = await seller.GetStringAsync($"/Disputes/Details/{id}");
        Assert.Contains("eBay sent a message.", sellerPage);
        Assert.Contains("Please share &lt;b&gt;tracking&lt;/b&gt;.", sellerPage);

        var decided = await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new()
            { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["ForBuyer"] = "true", ["Message"] = "Seller gave no tracking.", ["RefundAmount"] = "1", ["Status"] = "Open" })));
        Assert.Equal(HttpStatusCode.Redirect, decided.StatusCode);
        Assert.Equal("Closed|DecidedForBuyer|49.90", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT,'|',RefundAmount) FROM dbo.Dispute WHERE id={id}"));
        var after = WebUtility.HtmlDecode(await admin.GetStringAsync(url));
        foreach (var text in new[] { "Decision recorded. The request is closed.", "Closed — decided for the buyer", "This request is closed.", "Admin e05admin", "decided this request in the buyer's favor and refunded US $49.90 (simulated)" })
            Assert.Contains(text, after);
        Assert.DoesNotContain("Confirm decision", after);
        var again = await admin.PostAsync("/Admin/DisputeAct", new FormUrlEncodedContent(Form(page, new()
            { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["ForBuyer"] = "false", ["Message"] = "Changed my mind" })));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE DisputeId={id} AND Type='Decided'"));

        // Both parties see the outcome and the note; the admin's name stays internal.
        var buyerPage = WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Disputes/Details/{id}"));
        foreach (var text in new[] { "Closed — decided in your favor", "eBay decided this request in your favor and refunded US $49.90 (simulated).", "Seller gave no tracking.", "Refunded (simulated)" })
            Assert.Contains(text, buyerPage);
        Assert.DoesNotContain("e05admin", buyerPage);
        Assert.Contains("eBay decided this request in the buyer's favor", WebUtility.HtmlDecode(await seller.GetStringAsync($"/Disputes/Details/{id}")));
        Assert.Contains("Closed — decided in the buyer's favor", WebUtility.HtmlDecode(await seller.GetStringAsync("/Disputes")));
        Assert.Contains("Closed — decided in your favor", WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Orders/Details/{s.Order}")));
        Assert.Contains("No requests are waiting for a decision.", await admin.GetStringAsync("/Admin/Disputes"));
        Assert.Contains("Closed — decided for the buyer", WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Disputes?state=Closed")));

        using var remote = new AccountHttpTests.Factory(s.Target, "192.0.2.10");
        using var outside = await Login(remote, "e05admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync("/Admin/Disputes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(url)).StatusCode);
    }
}
