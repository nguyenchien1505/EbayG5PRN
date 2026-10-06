using System.Net;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>E03: request lists, details/timeline paging and replies over HTTP (DISPUTE_DETAILS.md, G5-07).</summary>
public sealed class DisputeDetailsTests
{
    private static readonly DisputeTiming Timing = new();

    // E05: the service treats a user as Admin only with a credential (like moderation); the seeded admin has none.
    private static async Task<SetupData> Setup()
    {
        var s = await ReviewTests.Setup();
        SqlFixture.Execute(s.Target, $"UPDATE dbo.[User] SET password=N'e05-admin-credential' WHERE id={s.Admin}");
        return s;
    }
    private static async Task<T> With<T>(SetupData s, Func<DisputeService, Task<T>> run)
    {
        await using var db = Open(s.Target);
        return await run(new DisputeService(db, TimeProvider.System, Timing));
    }
    private static void EstimatedDaysAgo(SetupData s, int order, int days) =>
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-{days},SYSUTCDATETIME()) WHERE id={order}");
    private static void DueNow(SetupData s, int dispute) => SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE id={dispute}");
    private static async Task<int> Place(SetupData s)
    {
        await using var db = Open(s.Target);
        return (await new CheckoutService(db, Timing).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()))).OrderId!.Value;
    }
    private static int Line(SetupData s, int order) => int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MIN(id) FROM dbo.OrderItem WHERE orderId={order}"));
    private static Task<DisputeResult> OpenInr(SetupData s, int line) =>
        With(s, x => x.OpenAsync(s.Buyer, new(line, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, null)));
    private static Task<DisputeResult> OpenReturn(SetupData s, int line, DisputeReason reason = DisputeReason.Damaged) =>
        With(s, x => x.OpenAsync(s.Buyer, new(line, DisputeKind.Return, reason, null, "Screen cracked on arrival")));
    private static Task<DisputeList?> List(SetupData s, int actor, DisputeKind? kind = null, DisputeListState? state = null, int page = 1) =>
        With(s, x => x.ListAsync(actor, new(kind, state, page)));
    private static string Events(SetupData s, int dispute) => SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE DisputeId={dispute}");

    [Fact]
    public async Task Lists_show_requests_the_buyer_raised_or_the_seller_received_with_filters_and_paging()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        for (var i = 0; i < 11; i++) SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},2,10.00)");
        var lines = SqlFixture.Scalar(s.Target, $"SELECT STRING_AGG(id,',') WITHIN GROUP (ORDER BY id) FROM dbo.OrderItem WHERE orderId={s.Order}").Split(',').Select(int.Parse).ToArray();
        var inr = new List<int>();
        foreach (var line in lines) inr.Add((await OpenInr(s, line)).DisputeId!.Value);
        var delivered = await Place(s);
        await Deliver(s, delivered);
        var ret = (await OpenReturn(s, Line(s, delivered))).DisputeId!.Value;
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(s.Buyer, inr[0], new(DisputeAction.Close)))).Code);
        // Legacy baseline rows (no order line) never appear in the module.
        SqlFixture.Execute(s.Target, $"INSERT dbo.Dispute(orderId,raisedBy,description,status) VALUES({s.Order},{s.Buyer},N'legacy','Open')");

        var all = inr.Append(ret).OrderDescending().ToArray();
        var page1 = (await List(s, s.Buyer))!;
        Assert.Equal((DisputeActor.Buyer, 13, 2), (page1.Viewer, page1.Total, page1.TotalPages));
        Assert.Equal(all.Take(10), page1.Items.Select(x => x.Id));
        var page2 = (await List(s, s.Buyer, page: 2))!;
        Assert.Equal(all.Skip(10), page2.Items.Select(x => x.Id));
        var beyond = (await List(s, s.Buyer, page: 3))!;
        Assert.Empty(beyond.Items); Assert.Equal(13, beyond.Total);

        var top = page1.Items[0];
        Assert.Equal((ret, delivered, DisputeKind.Return, DisputeReason.Damaged, DisputeStatus.Open, 49.90m, "seller", "Wireless Headphones"),
            (top.Id, top.OrderId, top.Kind, top.Reason, top.Status, top.LineTotal, top.Counterpart!, top.ItemTitle));
        Assert.Equal(20.00m, page1.Items.Single(x => x.Id == inr[^1]).LineTotal);
        Assert.Equal(new[] { ret }, (await List(s, s.Buyer, DisputeKind.Return))!.Items.Select(x => x.Id));
        var closed = Assert.Single((await List(s, s.Buyer, state: DisputeListState.Closed))!.Items);
        Assert.Equal((inr[0], DisputeOutcome.BuyerClosed), (closed.Id, closed.Outcome!.Value));
        Assert.Equal(12, (await List(s, s.Buyer, state: DisputeListState.Open))!.Total);
        Assert.Equal(11, (await List(s, s.Buyer, DisputeKind.ItemNotReceived, DisputeListState.Open))!.Total);

        var seller = (await List(s, s.Seller))!;
        Assert.Equal((DisputeActor.Seller, 13), (seller.Viewer, seller.Total));
        Assert.Equal(page1.Items.Select(x => x.Id), seller.Items.Select(x => x.Id));
        Assert.All(seller.Items, x => Assert.Equal("buyer", x.Counterpart));
        Assert.Equal(0, (await List(s, s.OtherSeller))!.Total);
        Assert.Equal(0, (await List(s, s.OtherBuyer))!.Total);
        Assert.Null(await List(s, s.Admin));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => List(s, s.Buyer, page: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => List(s, s.Buyer, (DisputeKind)7));

        var forSeller = await With(s, x => x.RequestsForOrderAsync(s.Seller, s.Order));
        Assert.Equal(lines.Order(), forSeller.Keys.Order());
        Assert.Equal(inr[0], Assert.Single(forSeller[lines[0]]).Id);
        Assert.Equal(DisputeStatus.Closed, forSeller[lines[0]][0].Status);
        Assert.Equal(lines.Length, (await With(s, x => x.RequestsForOrderAsync(s.Buyer, s.Order))).Count);
        Assert.Empty(await With(s, x => x.RequestsForOrderAsync(s.OtherSeller, s.Order)));
        Assert.Empty(await With(s, x => x.RequestsForOrderAsync(s.OtherBuyer, s.Order)));
        Assert.Empty(await With(s, x => x.RequestsForOrderAsync(s.Admin, s.Order)));
    }

    [Fact]
    public async Task Timeline_pages_newest_first_and_names_the_other_party()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var id = (await OpenInr(s, s.Item)).DisputeId!.Value;
        for (var i = 1; i <= 24; i++)
            Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(i % 2 == 0 ? s.Seller : s.Buyer, id, new(DisputeAction.Message, $"Message {i}")))).Code);

        var first = (await With(s, x => x.FindAsync(s.Buyer, id, 1)))!;
        Assert.Equal((25, 2, 1, 20), (first.EventTotal, first.EventPages, first.EventPage, first.Events.Count));
        Assert.Equal("Message 24", first.Events[0].Message);
        Assert.Equal(first.Events.Select(x => x.Id).OrderDescending(), first.Events.Select(x => x.Id));
        var second = (await With(s, x => x.FindAsync(s.Buyer, id, 2)))!;
        Assert.Equal(5, second.Events.Count);
        Assert.Equal(DisputeEventType.Opened, second.Events[^1].Type);
        Assert.Empty((await With(s, x => x.FindAsync(s.Buyer, id, 3)))!.Events);
        Assert.Equal(first.Events.Select(x => x.Id), (await With(s, x => x.FindAsync(s.Buyer, id)))!.Events.Select(x => x.Id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => With(s, x => x.FindAsync(s.Buyer, id, 0)));

        Assert.Equal(("seller", "buyer", "seller"), (first.Counterpart!, first.BuyerName!, first.SellerName!));
        Assert.Equal("buyer", (await With(s, x => x.FindAsync(s.Seller, id, 1)))!.Counterpart);
        var admin = (await With(s, x => x.FindAsync(s.Admin, id, 1)))!;
        Assert.Equal((DisputeActor.Admin, (string?)null), (admin.Viewer, admin.Counterpart));
        Assert.Null(await With(s, x => x.FindAsync(s.OtherBuyer, id, 1)));
        Assert.Null(await With(s, x => x.FindAsync(s.OtherSeller, id, 1)));
    }

    private static async Task<HttpClient> LoginAdmin(SetupData s, AccountHttpTests.Factory factory)
    {
        await using (var db = Open(s.Target))
        {
            Assert.True(await new EbayGroup5.Infrastructure.Authentication.AccountService(db).RegisterAsync("e03admin", "e03admin@d01.example.invalid", Password));
            await db.Set<User>().Where(x => x.Email == "E03ADMIN@D01.EXAMPLE.INVALID").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
        }
        return await Login(factory, "e03admin");
    }

    // Tokens belong to the signed-in user; step-1 pages only have GET forms, so fall back to the client's own account page.
    private static async Task<HttpResponseMessage> Post(HttpClient client, string tokenPage, Dictionary<string, string> fields)
    {
        var token = Field(tokenPage);
        fields["__RequestVerificationToken"] = token.Length > 0 ? token : Field(await client.GetStringAsync("/Account/Me"));
        return await client.PostAsync("/Disputes/Act", new FormUrlEncodedContent(fields));
    }

    private static async Task<string> Html(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url));

    [Fact]
    public async Task Http_return_flow_offer_accept_ship_refund_with_role_checks()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var id = (await OpenReturn(s, s.Item)).DisputeId!.Value;
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var seller = await Login(factory, "seller");
        using var stranger = await Login(factory, "otherbuyer");
        using var otherSeller = await Login(factory, "otherseller");
        using var admin = await LoginAdmin(s, factory);
        var url = $"/Disputes/Details/{id}";

        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Disputes")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/Disputes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherSeller.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Disputes?state=Bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Disputes?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync($"{url}?activity=0")).StatusCode);

        // Entry points and lists.
        Assert.Contains($"href=\"/Disputes/Details/{id}\">See request details</a>", await Html(buyer, "/Orders/Purchases"));
        Assert.Contains($"href=\"/Disputes/Details/{id}\"><strong>Return request</strong></a> · Waiting for the seller to respond by", await Html(buyer, $"/Orders/Details/{s.Order}"));
        Assert.Contains($"Return request</strong> · Respond by", await Html(seller, $"/Orders/Details/{s.Order}"));
        Assert.Contains($"href=\"/Disputes/Details/{id}\">See request details</a>", await Html(seller, $"/Orders/Details/{s.Order}"));
        var buyerList = await Html(buyer, "/Disputes");
        Assert.Contains("Returns and requests", buyerList); Assert.Contains("Requests you sent to sellers", buyerList);
        Assert.Contains("Waiting for the seller to respond by", buyerList); Assert.Contains("Seller: seller", buyerList);
        Assert.Contains($"href=\"/Disputes/Details/{id}\">See request details</a>", buyerList);
        Assert.DoesNotContain("Requests and disputes", buyerList);
        Assert.Contains("href=\"/Disputes\">Returns and requests</a>", buyerList);
        var sellerList = await Html(seller, "/Disputes?kind=Return");
        Assert.Contains("Requests and disputes", sellerList); Assert.Contains("Respond by", sellerList); Assert.Contains("Buyer: buyer", sellerList);
        Assert.Contains($"href=\"/Disputes/Details/{id}\">See details</a>", sellerList);
        Assert.Contains("No requests match this page or filter.", await Html(seller, "/Disputes?kind=ItemNotReceived"));
        Assert.DoesNotContain("Wireless Headphones", await Html(otherSeller, "/Disputes"));

        // D2 seller view: damaged item, so no Decline option; system/admin replies are rejected.
        var sellerPage = await Html(seller, url);
        foreach (var text in new[] { "You have a return request", "There's something wrong with the item your buyer's received, so they'd like to return it.", "Please respond by", "Info from the buyer", "Item damaged", "Screen cracked on arrival", "Select a reply",
                     "Accept the return", "Give a full refund", "Offer a partial refund", "Send the buyer a message", "You can reply to the buyer.", "Continue", "What happens from here", "Request activity",
                     "The buyer started a return." })
            Assert.Contains(text, sellerPage);
        Assert.DoesNotContain("Decline the return", sellerPage);
        Assert.DoesNotContain("Ask eBay to step in and help", sellerPage);
        Assert.Equal(HttpStatusCode.Conflict, (await seller.GetAsync($"{url}?reply=AcceptOffer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync($"{url}?reply=Decide")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync($"{url}?reply=Bogus")).StatusCode);
        var offerStep = await Html(seller, $"{url}?reply=OfferPartial");
        Assert.Contains("Amount (US $)", offerStep); Assert.Contains("Send offer", offerStep); Assert.Contains("less than US $49.90", offerStep);

        // CSRF, invalid amounts keep what was typed, and only the server decides the outcome.
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.PostAsync("/Disputes/Act", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["DisputeId"] = id.ToString(), ["Action"] = "Message", ["Message"] = "no token" }))).StatusCode);
        var notNumber = await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "OfferPartial", ["Amount"] = "abc", ["Message"] = "Typed note" });
        Assert.Equal(HttpStatusCode.BadRequest, notNumber.StatusCode);
        var notNumberHtml = WebUtility.HtmlDecode(await notNumber.Content.ReadAsStringAsync());
        Assert.Contains("Enter an amount such as 12.50.", notNumberHtml); Assert.Contains("Typed note", notNumberHtml);
        var tooMuch = await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "OfferPartial", ["Amount"] = "60" });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("less than US $49.90", WebUtility.HtmlDecode(await tooMuch.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Decide", ["Message"] = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Message", ["Message"] = "x" })).StatusCode); // Seller's token.
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(buyer, await buyer.GetStringAsync("/Account/Me"), new() { ["DisputeId"] = id.ToString(), ["Action"] = "AcceptReturn" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(otherSeller, await otherSeller.GetStringAsync("/Account/Me"), new() { ["DisputeId"] = id.ToString(), ["Action"] = "Refund" })).StatusCode);
        Assert.Equal("1", Events(s, id));

        var offered = await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "OfferPartial", ["Amount"] = "12.50", ["Message"] = "<script>alert(1)</script> Sorry!" });
        Assert.Equal((HttpStatusCode.Redirect, url), (offered.StatusCode, offered.Headers.Location!.OriginalString));
        var afterOffer = await seller.GetStringAsync(url);
        Assert.Contains("Your partial refund offer was sent.", afterOffer);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; Sorry!", afterOffer);
        Assert.DoesNotContain("<script>alert(1)</script>", afterOffer);
        Assert.Contains("You offered a partial refund of US $12.50.", WebUtility.HtmlDecode(afterOffer));

        var buyerPage = await Html(buyer, url);
        foreach (var text in new[] { "Your return request", "The seller offered a partial refund", "Accept the partial refund", "Decline the offer", "Send the seller a message",
                     "The seller offered a partial refund of US $12.50.", "Close your request", "You can ask eBay to step in after" })
            Assert.Contains(text, buyerPage);
        // Overposted amount/status are ignored; declining clears the offer.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(buyer, buyerPage, new() { ["DisputeId"] = id.ToString(), ["Action"] = "DeclineOffer", ["Amount"] = "1", ["Status"] = "Closed" })).StatusCode);
        Assert.Equal("Open||", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(status,'|',OfferAmount,'|',Outcome COLLATE DATABASE_DEFAULT) FROM dbo.Dispute WHERE id={id}"));

        Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "AcceptReturn" })).StatusCode);
        var events = Events(s, id);
        var again = await Post(seller, offerStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "AcceptReturn" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode); // A resubmitted reply writes nothing.
        Assert.Contains("isn't available for this request right now", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()));
        Assert.Equal(events, Events(s, id));
        Assert.Contains("Return accepted — buyer ships by", await Html(seller, "/Disputes"));

        var shipStep = await Html(buyer, $"{url}?reply=MarkReturnShipped");
        Assert.Contains("Tracking number", shipStep);
        var noTracking = await Post(buyer, shipStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "MarkReturnShipped", ["Message"] = "Sent today" });
        Assert.Equal(HttpStatusCode.BadRequest, noTracking.StatusCode);
        Assert.Contains("Sent today", await noTracking.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(buyer, shipStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "MarkReturnShipped", ["Tracking"] = "RT-123" })).StatusCode);
        Assert.Contains("Item shipped back — refund by", await Html(seller, "/Disputes"));
        var refundStep = await Html(seller, $"{url}?reply=Refund");
        Assert.Contains("Refund US $49.90", refundStep);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, refundStep, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Refund" })).StatusCode);
        Assert.Equal("Closed|Refunded|49.90|RT-123", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT,'|',RefundAmount,'|',ReturnTracking COLLATE DATABASE_DEFAULT) FROM dbo.Dispute WHERE id={id}"));

        var closed = await Html(buyer, url);
        foreach (var text in new[] { "Closed — refunded", "This request is closed.", "Refunded (simulated)", "Return tracking", "RT-123", "You started a return.",
                     "The seller refunded US $49.90 (simulated).", "You shipped the item back.", "The seller accepted the return.", "You declined the partial refund offer of US $12.50." })
            Assert.Contains(text, closed);
        Assert.DoesNotContain("Select a reply", closed);
        Assert.DoesNotContain("Close your request", closed);
        Assert.Contains("Closed — refunded", await Html(buyer, "/Disputes?state=Closed"));
        Assert.Contains("No requests match this page or filter.", await Html(buyer, "/Disputes?state=Open"));
    }

    [Fact]
    public async Task Http_item_not_received_escalates_and_closes_with_a_reason()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var id = (await OpenInr(s, s.Item)).DisputeId!.Value;
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var buyer = await Login(factory, "buyer");
        using var seller = await Login(factory, "seller");
        var url = $"/Disputes/Details/{id}";

        var sellerPage = await Html(seller, url);
        foreach (var text in new[] { "You have an item not received request", "Your buyer hasn't received their item.", "The buyer would like", "A refund", "Give a full refund", "Provide tracking information", "The buyer reported that the item didn't arrive." })
            Assert.Contains(text, sellerPage);
        Assert.DoesNotContain("Accept the return", sellerPage);
        var before = await Html(buyer, url);
        Assert.Contains("You can ask eBay to step in after", before);
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.GetAsync($"{url}?reply=Escalate")).StatusCode);

        DueNow(s, id);
        var due = await Html(buyer, url);
        Assert.Contains($"href=\"{url}?reply=Escalate\">Ask eBay to step in and help</a>", due);
        var step = await Html(buyer, $"{url}?reply=Escalate");
        foreach (var text in new[] { "What's wrong?", "The seller hasn't responded", "I haven't received a refund", "I don't agree with the seller's response", "Something else", "Confirm" })
            Assert.Contains(text, step);
        var noReason = await Post(buyer, step, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Escalate", ["Message"] = "Still nothing" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var noReasonHtml = WebUtility.HtmlDecode(await noReason.Content.ReadAsStringAsync());
        Assert.Contains("Choose a reason.", noReasonHtml); Assert.Contains("Still nothing", noReasonHtml);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, step, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Escalate", ["Reason"] = "Bogus" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(buyer, step, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Escalate", ["Reason"] = "NoResponse", ["Message"] = new string('x', 1901) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(seller, sellerPage, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Escalate", ["Reason"] = "NoResponse" })).StatusCode);
        Assert.Equal("1", Events(s, id));

        Assert.Equal(HttpStatusCode.Redirect, (await Post(buyer, step, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Escalate", ["Reason"] = "NoResponse", ["Message"] = "  Still nothing  " })).StatusCode);
        Assert.Equal("Escalated|The seller hasn't responded\n\nStill nothing", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(d.status,'|',e.Message COLLATE DATABASE_DEFAULT) FROM dbo.Dispute d JOIN dbo.DisputeEvent e ON e.DisputeId=d.id AND e.Type='Escalated' WHERE d.id={id}"));
        var escalated = await Html(buyer, url);
        foreach (var text in new[] { "You asked eBay to step in. We usually respond within 48 hours.", "eBay is reviewing this request", "You asked eBay to step in.", "The seller hasn't responded" })
            Assert.Contains(text, escalated);
        Assert.DoesNotContain("Ask eBay to step in and help</a>", escalated);
        var sellerEscalated = await Html(seller, url);
        Assert.Contains("eBay is reviewing this request", sellerEscalated);
        Assert.Contains("The buyer asked eBay to step in.", sellerEscalated);
        Assert.Contains("Give a full refund", sellerEscalated); // E01: the seller may still refund while eBay reviews.

        var close = await Html(buyer, $"{url}?reply=Close");
        Assert.Contains("Why are you closing this request?", close); Assert.Contains("My problem was resolved", close);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(buyer, close, new() { ["DisputeId"] = id.ToString(), ["Action"] = "Close", ["Reason"] = "Resolved" })).StatusCode);
        Assert.Equal("Closed|BuyerClosed|My problem was resolved", SqlFixture.Scalar(s.Target,
            $"SELECT CONCAT(d.status,'|',d.Outcome COLLATE DATABASE_DEFAULT,'|',e.Message COLLATE DATABASE_DEFAULT) FROM dbo.Dispute d JOIN dbo.DisputeEvent e ON e.DisputeId=d.id AND e.Type='Closed' WHERE d.id={id}"));
        Assert.Contains("You closed this request", await Html(buyer, "/Disputes"));
        Assert.Contains("Buyer closed this request", await Html(seller, "/Disputes?kind=ItemNotReceived"));
        Assert.Contains("The buyer closed the request.", await Html(seller, url));
    }
}
