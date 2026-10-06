using System.Net;
using System.Text.Json;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using Microsoft.Data.SqlClient;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>E02: request forms and server drafts (DISPUTE_FORMS.md).</summary>
public sealed class DisputeFormTests
{
    private static readonly DisputeTiming Timing = new();
    private static DisputeService Service(EbayGroup5.Infrastructure.Persistence.EbayDbContext db) => new(db, TimeProvider.System, Timing);
    private static async Task<T> With<T>(SetupData s, Func<DisputeService, Task<T>> run)
    {
        await using var db = Open(s.Target);
        return await run(Service(db));
    }
    private static void EstimatedDaysAgo(SetupData s, int order, int days) =>
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-{days},SYSUTCDATETIME()) WHERE id={order}");
    private static string Drafts(SetupData s) => SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.DisputeDraft");
    private static async Task<int> Place(SetupData s)
    {
        await using var db = Open(s.Target);
        return (await new CheckoutService(db, Timing).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()))).OrderId!.Value;
    }
    private static int Line(SetupData s, int order) => int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MIN(id) FROM dbo.OrderItem WHERE orderId={order}"));
    private static DisputeDraftInput Inr(int line, DisputePreference? preference = DisputePreference.Refund, string? details = "Box never came") =>
        new(line, DisputeKind.ItemNotReceived, null, preference, details);

    [Fact]
    public async Task Drafts_upsert_for_the_owner_only_while_openable_and_go_away_only_when_the_request_opens()
    {
        var s = await Setup();
        Assert.Equal(DisputeResultCode.Conflict, (await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item)))).Code); // Before the estimated delivery date.
        EstimatedDaysAgo(s, s.Order, 1);
        var saved = await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item, details: "  Box never came  ")));
        Assert.Equal(DisputeResultCode.Success, saved.Code); Assert.NotNull(saved.SavedAtUtc);
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item, DisputePreference.Item, "Still waiting")))).Code);
        Assert.Equal("1", Drafts(s));
        var draft = (await With(s, x => x.FindDraftAsync(s.Buyer, s.Item, DisputeKind.ItemNotReceived)))!;
        Assert.Equal((DisputePreference.Item, "Still waiting", (DisputeReason?)null), (draft.Preference!.Value, draft.Details!, draft.Reason));
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item, null, "   ")))).Code); // Empty fields are stored as NULL.
        Assert.Equal("||ItemNotReceived", SqlFixture.Scalar(s.Target, "SELECT CONCAT(Preference COLLATE DATABASE_DEFAULT,'|',Details,'|',Kind COLLATE DATABASE_DEFAULT) FROM dbo.DisputeDraft"));

        Assert.Null(await With(s, x => x.FindDraftAsync(s.OtherBuyer, s.Item, DisputeKind.ItemNotReceived)));
        Assert.Null(await With(s, x => x.FindDraftAsync(s.Buyer, s.Item, DisputeKind.Return)));
        Assert.Equal(DisputeResultCode.NotFound, (await With(s, x => x.SaveDraftAsync(s.OtherBuyer, Inr(s.Item)))).Code);
        Assert.Equal(DisputeResultCode.Forbidden, (await With(s, x => x.SaveDraftAsync(s.Seller, Inr(s.Item)))).Code);
        Assert.Equal(DisputeResultCode.Conflict, (await With(s, x => x.SaveDraftAsync(s.Buyer, new(s.Item, DisputeKind.Return, DisputeReason.Damaged, null, "x")))).Code);
        foreach (var bad in new[]
        {
            new DisputeDraftInput(s.Item, DisputeKind.ItemNotReceived, DisputeReason.Damaged, null, null),
            new DisputeDraftInput(s.Item, DisputeKind.Return, null, DisputePreference.Refund, null),
            new DisputeDraftInput(s.Item, (DisputeKind)9, null, null, null),
            Inr(s.Item, details: new string('x', 2001)),
            Inr(s.Item, details: "bad\u0007bell"),
        })
            Assert.Equal(DisputeResultCode.Invalid, (await With(s, x => x.SaveDraftAsync(s.Buyer, bad))).Code);

        // A failed open keeps the draft; a successful open deletes it in the same transaction.
        await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item, details: "Keep me")));
        EstimatedDaysAgo(s, s.Order, 31);
        Assert.Equal(DisputeResultCode.Conflict, (await With(s, x => x.OpenAsync(s.Buyer, new(s.Item, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, "Keep me")))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await With(s, x => x.OpenAsync(s.Buyer, new(s.Item, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, null, "Keep me")))).Code);
        Assert.Equal("1", Drafts(s));
        EstimatedDaysAgo(s, s.Order, 1);
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.OpenAsync(s.Buyer, new(s.Item, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, "Keep me")))).Code);
        Assert.Equal("0", Drafts(s));
        // A late autosave after the request opened cannot bring the draft back.
        Assert.Equal(DisputeResultCode.Conflict, (await With(s, x => x.SaveDraftAsync(s.Buyer, Inr(s.Item)))).Code);
        Assert.Equal("0", Drafts(s));
    }

    [Fact]
    public async Task Batch_eligibility_matches_single_line_results_and_lists_requests()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var delivered = await Place(s); await Deliver(s, delivered);
        var pending = await Place(s);
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{s.Product},2,10.00)");
        var lines = new[] { s.Item, Line(s, delivered), Line(s, pending), int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MAX(id) FROM dbo.OrderItem WHERE orderId={s.Order}")) };
        var opened = (await With(s, x => x.OpenAsync(s.Buyer, new(s.Item, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Item, null)))).DisputeId!.Value;

        var batch = await With(s, x => x.EligibilityAsync(s.Buyer, lines.Append(int.MaxValue).Append(s.Item).ToArray()));
        Assert.Equal(lines.Order(), batch.Keys.Order());
        foreach (var line in lines)
        {
            var single = (await With(s, x => x.EligibilityAsync(s.Buyer, line)))!;
            Assert.Equal(single.ItemNotReceived, batch[line].ItemNotReceived);
            Assert.Equal(single.Return, batch[line].Return);
            Assert.Equal((single.ItemTitle, single.LineTotal, single.EstimatedDeliveryUtc), (batch[line].ItemTitle, batch[line].LineTotal, batch[line].EstimatedDeliveryUtc));
            Assert.Equal(single.Requests, batch[line].Requests);
        }
        Assert.Equal(("Wireless Headphones", 49.90m), (batch[s.Item].ItemTitle, batch[s.Item].LineTotal!.Value));
        Assert.Equal(20.00m, batch[lines[3]].LineTotal);
        var request = Assert.Single(batch[s.Item].Requests);
        Assert.Equal((opened, DisputeKind.ItemNotReceived, DisputeStatus.Open, (DisputeOutcome?)null), (request.Id, request.Kind, request.Status, request.Outcome));
        Assert.True(batch[lines[3]].ItemNotReceived.CanOpen); // Requests are per line: the other line of the order can still be reported.
        Assert.True(batch[lines[1]].Return.CanOpen);
        Assert.False(batch[lines[2]].ItemNotReceived.CanOpen);
        Assert.Empty(await With(s, x => x.EligibilityAsync(s.OtherBuyer, lines)));
        Assert.Empty(await With(s, x => x.EligibilityAsync(s.Seller, lines)));
        await Assert.ThrowsAsync<ArgumentException>(() => With(s, x => x.EligibilityAsync(s.Buyer, Enumerable.Range(1, 51).ToArray())));
    }

    [Fact]
    public async Task Draft_table_constraints_and_grants()
    {
        var s = await Setup();
        string Insert(string kind, string reason = "NULL", string pref = "NULL", string details = "NULL", int? buyer = null) =>
            $"INSERT dbo.DisputeDraft(BuyerId,OrderItemId,Kind,Reason,Preference,Details) VALUES({buyer ?? s.Buyer},{s.Item},{kind},{reason},{pref},{details})";
        SqlFixture.Execute(s.Target, Insert("'Return'", "'Damaged'", details: "N'ok'"));
        foreach (var sql in new[]
        {
            Insert("'Return'"), // Duplicate buyer/line/kind.
            Insert("'ItemNotReceived'", "'Damaged'"),
            Insert("'ItemNotReceived'", pref: "'refund'"),
            Insert("'Return'", pref: "'Refund'", buyer: s.OtherBuyer),
            Insert("'Other'", buyer: s.OtherBuyer),
            Insert("'ItemNotReceived'", details: "N'   '"),
            $"INSERT dbo.DisputeDraft(BuyerId,OrderItemId,Kind) VALUES({s.Buyer},2147483647,'Return')",
        })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, sql));
        SqlFixture.Execute(s.Target, Insert("'ItemNotReceived'", "'ItemNotReceived'", "'Item'", "N'fine'"));
        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, "UPDATE dbo.DisputeDraft SET Details=N'edited'; DELETE dbo.DisputeDraft WHERE Kind='Return';");
        Assert.Equal("1", SqlFixture.Scalar(app, "SELECT COUNT(*) FROM dbo.DisputeDraft"));
    }

    private static Dictionary<string, string> Form(string html, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Field(html);
        return fields;
    }

    [Fact]
    public async Task Http_item_not_received_form_restores_drafts_and_a_resubmission_creates_one_request()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var stranger = await Login(factory, "otherbuyer");
        using var seller = await Login(factory, "seller");
        var url = $"/Disputes/NotReceived?orderItemId={s.Item}";
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Disputes/NotReceived?orderItemId=0")).StatusCode);
        var early = await buyer.GetAsync(url);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("after its estimated delivery date", WebUtility.HtmlDecode(await early.Content.ReadAsStringAsync()));
        Assert.DoesNotContain("I didn't receive it", WebUtility.HtmlDecode(await buyer.GetStringAsync("/Orders/Purchases")));

        EstimatedDaysAgo(s, s.Order, 1);
        var purchases = WebUtility.HtmlDecode(await buyer.GetStringAsync("/Orders/Purchases"));
        Assert.Contains($"href=\"/Disputes/NotReceived?orderItemId={s.Item}\">I didn't receive it</a>", purchases);
        Assert.DoesNotContain("Return this item", purchases);
        Assert.Contains($"href=\"/Disputes/NotReceived?orderItemId={s.Item}\">I didn't receive it</a>", WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Orders/Details/{s.Order}")));
        var page = WebUtility.HtmlDecode(await buyer.GetStringAsync(url));
        Assert.Contains("Report an item you didn't receive", page);
        Assert.Contains("I'd like a refund", page); Assert.Contains("I'd still like the item", page);
        Assert.Contains("What happens from here", page); Assert.Contains("Seller has 3 business days", page);
        Assert.Contains("/js/dispute-form.js", page);
        Assert.DoesNotContain("Draft restored", page);

        // Draft endpoint: CSRF required, owner only, JSON result.
        var noToken = await buyer.PostAsync("/Disputes/Draft", new FormUrlEncodedContent(new Dictionary<string, string> { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var draft = await buyer.PostAsync("/Disputes/Draft", new FormUrlEncodedContent(Form(page, new()
            { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived", ["Preference"] = "Item", ["Details"] = "<b>Tracking stopped</b> in Hanoi" })));
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.True(JsonDocument.Parse(await draft.Content.ReadAsStringAsync()).RootElement.TryGetProperty("savedAtUtc", out _));
        var strangerPage = await stranger.GetStringAsync("/Account/Me");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync("/Disputes/Draft", new FormUrlEncodedContent(Form(strangerPage, new()
            { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived" })))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.PostAsync("/Disputes/Draft", new FormUrlEncodedContent(Form(page, new()
            { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Bogus" })))).StatusCode);

        // Reload restores the draft (encoded, never as markup).
        var restored = await buyer.GetStringAsync(url);
        Assert.Contains("&lt;b&gt;Tracking stopped&lt;/b&gt; in Hanoi", restored);
        Assert.DoesNotContain("<b>Tracking stopped</b>", restored);
        Assert.Contains("Draft restored", restored);
        Assert.Matches("value=\"Item\"[^>]*checked", restored);

        // Invalid submit keeps what was typed; then the same form posted twice yields one request.
        var invalid = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(restored, new()
            { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived", ["Details"] = "No option chosen" })));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("No option chosen", await invalid.Content.ReadAsStringAsync());
        var fields = new Dictionary<string, string> { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived", ["Preference"] = "Refund", ["Details"] = "Never arrived", ["SellerId"] = s.OtherSeller.ToString(), ["Status"] = "Closed" };
        var first = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(restored, new(fields))));
        var second = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(restored, new(fields))));
        Assert.Equal((HttpStatusCode.Redirect, HttpStatusCode.Redirect), (first.StatusCode, second.StatusCode));
        Assert.Equal($"/Orders/Details/{s.Order}#line-{s.Item}", first.Headers.Location!.OriginalString);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal($"1|{s.Seller}|Open|Refund|Never arrived", SqlFixture.Scalar(s.Target, "SELECT CONCAT(COUNT(*) OVER(),'|',SellerId,'|',status,'|',Preference COLLATE DATABASE_DEFAULT,'|',description) FROM dbo.Dispute"));
        Assert.Equal("0", Drafts(s));
        var details = WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Orders/Details/{s.Order}"));
        Assert.Contains("This request was already sent.", details); // Notice of the second (resubmitted) post.
        Assert.Contains("Item not received request</strong></a> · Waiting for the seller to respond by", details);
        Assert.DoesNotContain("I didn't receive it</a>", details);
        purchases = WebUtility.HtmlDecode(await buyer.GetStringAsync("/Orders/Purchases"));
        Assert.Contains($"href=\"/Disputes/Details/{SqlFixture.Scalar(s.Target, "SELECT id FROM dbo.Dispute")}\">See request details</a>", purchases);
        Assert.DoesNotContain("I didn't receive it</a>", purchases);
        var again = await buyer.GetAsync(url);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Waiting for the seller to respond by", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Http_return_form_has_the_ebay_reasons_two_steps_and_requires_details()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var buyer = await Login(factory, "buyer");
        var url = $"/Disputes/Return?orderItemId={s.Item}";
        Assert.Contains($"href=\"/Disputes/Return?orderItemId={s.Item}\">Return this item</a>", WebUtility.HtmlDecode(await buyer.GetStringAsync("/Orders/Purchases")));
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.GetAsync($"/Disputes/NotReceived?orderItemId={s.Item}")).StatusCode);
        var page = WebUtility.HtmlDecode(await buyer.GetStringAsync(url));
        foreach (var label in new[] { "Ordered the wrong item", "Changed my mind", "No longer needed", "Found a better price", "Doesn't fit", "Item damaged",
                     "Item defective", "Missing parts or pieces", "Item didn't match description", "Received the wrong item" })
            Assert.Contains($"<span>{label}</span>", page);
        Assert.Contains("Why are you returning this item?", page); Assert.Contains("Give us a little more info.", page);
        Assert.Contains("Confirm return", page); Assert.Contains("30-day returns.", page); Assert.Contains("Photos can't be added in this demo.", page);
        Assert.Contains("data-change-of-mind=\"true\"", page);

        var noReason = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(page, new() { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Return", ["Details"] = "Screen cracked" })));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Contains("Screen cracked", await noReason.Content.ReadAsStringAsync());
        var noDetails = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(page, new() { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Return", ["Reason"] = "Damaged" })));
        Assert.Equal(HttpStatusCode.BadRequest, noDetails.StatusCode);
        Assert.Contains("Add details about the problem.", await noDetails.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(page, new() { ["OrderItemId"] = s.Item.ToString(), ["Reason"] = "Damaged", ["Details"] = "x" })))).StatusCode); // Kind is required.

        Assert.Equal(HttpStatusCode.OK, (await buyer.PostAsync("/Disputes/Draft", new FormUrlEncodedContent(Form(page, new() { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Return", ["Reason"] = "Damaged", ["Details"] = "Half typed" })))).StatusCode);
        var restored = await buyer.GetStringAsync(url);
        Assert.Matches("value=\"Damaged\"[^>]*checked", restored); Assert.Contains("Half typed", restored);
        var opened = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(Form(restored, new() { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Return", ["Reason"] = "Damaged", ["Details"] = "Screen cracked on arrival" })));
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        var details = WebUtility.HtmlDecode(await buyer.GetStringAsync(opened.Headers.Location!.OriginalString));
        Assert.Contains("Your request was sent. The seller has until", details);
        Assert.Contains("Return request</strong></a> · Waiting for the seller to respond by", details);
        Assert.Equal("Return|Damaged|Screen cracked on arrival|0", SqlFixture.Scalar(s.Target, "SELECT CONCAT(Kind COLLATE DATABASE_DEFAULT,'|',Reason COLLATE DATABASE_DEFAULT,'|',description,'|',(SELECT COUNT(*) FROM dbo.DisputeDraft)) FROM dbo.Dispute"));
    }
}
