using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Web.Controllers;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>F04 (SECURITY_TESTS.md): endpoint inventory, IDOR on writes, CSRF, stored XSS, headers and auth on one host with real SQL.</summary>
public sealed class SecurityTests
{
    // Every user-text field gets "xss-<field>" plus this payload: the marker proves the field rendered on a page, the payload must stay text.
    private const string Payload = "<img src=x onerror=alert(1)><script>alert(1)</script>";
    private static readonly string[] RawSignatures = ["<img src=x onerror", "<script>alert(1)", "<svg onload"];
    private static string Xss(string field) => $"xss-{field} {Payload}";
    private const string Header = "X-CSRF-TOKEN";

    /// <summary>
    /// Every action and hub method with its verb and access rule. A new endpoint fails this test until it is classified here and
    /// covered by the CSRF/IDOR sweeps below (unsafe verbs) — F04 keeps the attack surface explicit.
    /// </summary>
    private static readonly string[] Inventory =
    [
        "Account.Details GET auth", "Account.Login GET public", "Account.Login POST public", "Account.Logout POST auth", "Account.Me GET auth",
        "Account.Register GET public", "Account.Register POST public",
        "Admin.Dispute GET policy:AdminDemo", "Admin.DisputeAct POST policy:AdminDemo", "Admin.Disputes GET policy:AdminDemo",
        "Admin.Moderate POST policy:AdminDemo", "Admin.Moderation GET policy:AdminDemo", "Admin.ModerationItem GET policy:AdminDemo",
        "CatalogApi.Detail GET public", "CatalogApi.Search GET public",
        "Catalog.Details GET public", "Catalog.Index GET public", "Catalog.Reviews GET public",
        "ChatApi.Conversation GET auth", "ChatApi.History GET auth", "ChatApi.Inbox GET auth+roles:Buyer,Seller", "ChatApi.Open POST auth+policy:Buyer",
        "ChatApi.Read POST auth", "ChatApi.Send POST auth", "ChatApi.Token GET auth",
        "Checkout.AddAddress GET policy:Buyer", "Checkout.AddAddress POST policy:Buyer", "Checkout.Confirmation GET policy:Buyer",
        "Checkout.Index GET policy:Buyer", "Checkout.Place POST policy:Buyer",
        "Disputes.Act POST roles:Buyer,Seller", "Disputes.Details GET roles:Buyer,Seller", "Disputes.Draft POST roles:Buyer,Seller+policy:Buyer",
        "Disputes.Index GET roles:Buyer,Seller", "Disputes.NotReceived GET roles:Buyer,Seller+policy:Buyer",
        "Disputes.Open POST roles:Buyer,Seller+policy:Buyer", "Disputes.Return GET roles:Buyer,Seller+policy:Buyer",
        "Help.Unavailable GET public",
        "Home.Index ANY public", "Home.Search GET public", "Home.Status GET public",
        "Messages.Index GET roles:Buyer,Seller",
        "Orders.Details GET roles:Buyer,Seller", "Orders.Purchases GET roles:Buyer,Seller+policy:Buyer", "Orders.Receive POST roles:Buyer,Seller+policy:Buyer",
        "Orders.Sales GET roles:Buyer,Seller+policy:Seller", "Orders.Ship POST roles:Buyer,Seller+policy:Seller",
        "Reports.Create GET roles:Buyer,Seller", "Reports.Create POST roles:Buyer,Seller",
        "Reviews.Create GET policy:Buyer", "Reviews.Create POST policy:Buyer",
        "Sellers.Feedback GET public", "Sellers.Reply POST policy:Seller",
        "ChatHub.JoinConversation HUB roles:Buyer,Seller", "ChatHub.LeaveConversation HUB roles:Buyer,Seller",
        "ChatHub.MarkRead HUB roles:Buyer,Seller", "ChatHub.SendMessage HUB roles:Buyer,Seller",
    ];

    private static string Access(IEnumerable<AuthorizeAttribute> rules, bool anonymous)
    {
        if (anonymous) return "public";
        var parts = rules.Select(a => a.Policy != null ? "policy:" + a.Policy : a.Roles != null ? "roles:" + a.Roles : "auth").ToList();
        return parts.Count == 0 ? "public" : string.Join("+", parts);
    }

    [Fact]
    public void Every_endpoint_is_classified_and_none_skips_antiforgery()
    {
        var web = typeof(AccountController).Assembly;
        var found = new List<string>();
        foreach (var type in web.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.Name.EndsWith("Controller")))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null))
            {
                var verbs = method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Distinct().ToList();
                var rules = type.GetCustomAttributes<AuthorizeAttribute>().Concat(method.GetCustomAttributes<AuthorizeAttribute>());
                var anonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() != null || type.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                found.Add($"{type.Name[..^"Controller".Length]}.{method.Name} {(verbs.Count == 0 ? "ANY" : string.Join(",", verbs))} {Access(rules, anonymous)}");
                Assert.True(method.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() == null, $"{type.Name}.{method.Name} skips antiforgery.");
            }
        foreach (var type in web.GetTypes().Where(t => typeof(Hub).IsAssignableFrom(t) && !t.IsAbstract))
        {
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                if (method.Name is not ("OnConnectedAsync" or "OnDisconnectedAsync"))
                    found.Add($"{type.Name}.{method.Name} HUB {Access(type.GetCustomAttributes<AuthorizeAttribute>().Concat(method.GetCustomAttributes<AuthorizeAttribute>()), false)}");
        }
        foreach (var type in web.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
            Assert.True(type.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() == null, $"{type.Name} skips antiforgery.");
        var missing = Inventory.Except(found).ToList();
        var unexpected = found.Except(Inventory).ToList();
        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "Endpoint inventory changed (SECURITY_TESTS.md)." + Environment.NewLine + "Unclassified: " + string.Join("; ", unexpected) + Environment.NewLine + "Gone: " + string.Join("; ", missing));
    }

    [Fact]
    public void Global_antiforgery_filter_covers_every_unsafe_mvc_and_api_action()
    {
        using var factory = new AccountHttpTests.Factory(SqlFixture.NewTarget());
        var options = factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Contains(options.Filters, f => f is AutoValidateAntiforgeryTokenAttribute);
    }

    // ---- shared world: three accounts per side, real orders, one request, one conversation and every user-text field via HTTP ----

    private sealed class World : IDisposable
    {
        public required SetupData S { get; init; }
        public required AccountHttpTests.Factory Factory { get; init; }
        public required HttpClient Anonymous, Buyer, OtherBuyer, Seller, OtherSeller, Admin;
        public int OpenOrder, Dispute, Conversation, Review, Feedback, XssAddress;
        public void Dispose()
        {
            foreach (var client in new[] { Anonymous, Buyer, OtherBuyer, Seller, OtherSeller, Admin }) client.Dispose();
            Factory.Dispose();
        }
    }

    private static async Task<string> Token(HttpClient client) => Field(await client.GetStringAsync("/Account/Me"));

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, Dictionary<string, string> form, string? token)
    {
        var fields = new Dictionary<string, string>(form);
        if (token != null) fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string url, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (token != null) request.Headers.Add(Header, token);
        return await client.SendAsync(request);
    }

    private static async Task<string> ChatToken(HttpClient client) =>
        (await client.GetFromJsonAsync<ChatTokenDto>("/api/v1/chat/antiforgery"))!.RequestToken;
    private sealed record ChatTokenDto(string RequestToken);

    private static async Task Expect(HttpStatusCode status, Task<HttpResponseMessage> call, string what)
    {
        using var response = await call;
        Assert.True(response.StatusCode == status, $"{what}: expected {(int)status}, got {(int)response.StatusCode}");
    }

    private static async Task<World> Build()
    {
        var s = await Setup();
        await using (var db = Open(s.Target))
        {
            Assert.True(await new AccountService(db).RegisterAsync("f04admin", "f04admin@d01.example.invalid", Password));
            await db.Set<User>().Where(x => x.Email == "F04ADMIN@D01.EXAMPLE.INVALID").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
            // The display name is shown to sellers, admins and in public reviews; Register stores the same column (checked separately).
            await db.Set<User>().Where(x => x.Id == s.Buyer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Username, Xss("name")));
        }
        await Deliver(s, s.Order);
        int openOrder;
        await using (var db = Open(s.Target))
            openOrder = (await new CheckoutService(db).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()))).OrderId!.Value;

        // Sign-ins exceed the default 10/min auth window for one address; F04 checks that window separately.
        var factory = new AccountHttpTests.Factory(s.Target, settings: new Dictionary<string, string> { ["RateLimits:auth:PermitLimit"] = "200" });
        var w = new World
        {
            S = s, Factory = factory, OpenOrder = openOrder,
            Anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }),
            Buyer = await Login(factory, "buyer"), OtherBuyer = await Login(factory, "otherbuyer"),
            Seller = await Login(factory, "seller"), OtherSeller = await Login(factory, "otherseller"), Admin = await Login(factory, "f04admin"),
        };

        // Every user-text field enters through its real endpoint.
        await Expect(HttpStatusCode.Redirect, Post(w.Buyer, "/Checkout/AddAddress", new()
        {
            ["ProductId"] = s.Product.ToString(), ["Quantity"] = "1", ["FullName"] = Xss("addr-name"), ["Phone"] = "0123456789",
            ["Street"] = Xss("addr-street"), ["City"] = "Hanoi", ["Country"] = "Vietnam"
        }, await Token(w.Buyer)), "add address");
        w.XssAddress = int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MAX(id) FROM dbo.Address WHERE userId={s.Buyer}"));
        await Expect(HttpStatusCode.Redirect, Post(w.Buyer, "/Reviews/Create", new()
        {
            ["OrderItemId"] = s.Item.ToString(), ["Rating"] = "4", ["Comment"] = Xss("review"), ["Sentiment"] = "Positive"
        }, await Token(w.Buyer)), "review + feedback");
        w.Review = int.Parse(SqlFixture.Scalar(s.Target, "SELECT MAX(id) FROM dbo.Review"));
        w.Feedback = int.Parse(SqlFixture.Scalar(s.Target, "SELECT MAX(Id) FROM dbo.SellerFeedback"));
        await Expect(HttpStatusCode.Redirect, Post(w.Seller, "/Sellers/Reply", new() { ["feedbackId"] = w.Feedback.ToString(), ["reply"] = Xss("reply") },
            await Token(w.Seller)), "seller reply");
        await Expect(HttpStatusCode.Redirect, Post(w.Buyer, "/Disputes/Open", new()
        {
            ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "Return", ["Reason"] = "Damaged", ["Details"] = Xss("dispute")
        }, await Token(w.Buyer)), "open return");
        w.Dispute = int.Parse(SqlFixture.Scalar(s.Target, "SELECT MAX(Id) FROM dbo.Dispute"));
        await Expect(HttpStatusCode.Redirect, Post(w.Seller, "/Disputes/Act", new()
        {
            ["DisputeId"] = w.Dispute.ToString(), ["Action"] = "Message", ["Message"] = Xss("dispute-msg")
        }, await Token(w.Seller)), "seller message on request");
        await Expect(HttpStatusCode.Redirect, Post(w.Seller, "/Reports/Create", new()
        {
            ["kind"] = "review", ["id"] = w.Review.ToString(), ["reason"] = "Spam", ["details"] = Xss("report")
        }, await Token(w.Seller)), "report review");
        await Expect(HttpStatusCode.Redirect, Post(w.Admin, "/Admin/Moderate", new()
        {
            ["kind"] = "review", ["id"] = w.Review.ToString(), ["action"] = "Dismiss", ["note"] = Xss("note")
        }, await Token(w.Admin)), "admin dismiss with note");
        var chat = await ChatToken(w.Buyer);
        using (var opened = await PostJson(w.Buyer, "/api/v1/chat/conversations", new { productId = s.Product }, chat))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            w.Conversation = (await opened.Content.ReadFromJsonAsync<ChatOpenResult>())!.Conversation.Id;
        }
        await Expect(HttpStatusCode.OK, PostJson(w.Buyer, $"/api/v1/chat/conversations/{w.Conversation}/messages",
            new ChatSendCommand(Guid.NewGuid(), Xss("chat"), s.Product), chat), "chat send");
        return w;
    }

    /// <summary>Counts and checksums of every table a write endpoint can touch; equal before/after means nothing changed.</summary>
    private static string Fingerprint(string target)
    {
        string[] tables = ["[User]", "Address", "OrderTable", "OrderItem", "Payment", "ShippingInfo", "Inventory", "Product", "Review", "SellerFeedback",
            "Dispute", "DisputeEvent", "DisputeDraft", "Message", "ChatConversation", "ContentReport", "ModerationAudit"];
        return SqlFixture.Scalar(target, "SELECT CONCAT_WS('|'," + string.Join(",", tables.Select(t =>
            $"(SELECT CONCAT('{t}=',COUNT_BIG(*),':',CHECKSUM_AGG(BINARY_CHECKSUM(*))) FROM dbo.{t})")) + ")");
    }

    [Fact]
    public async Task Writes_on_other_users_objects_are_refused_and_change_nothing()
    {
        using var w = await Build();
        var s = w.S;
        var before = Fingerprint(s.Target);
        var mismatches = new List<string>();
        async Task Probe(string what, HttpStatusCode expected, Task<HttpResponseMessage> call)
        {
            using var response = await call;
            if (response.StatusCode != expected) mismatches.Add($"{what}: expected {(int)expected}, got {(int)response.StatusCode}");
        }
        string Id(int value) => value.ToString();
        var otherBuyer = await Token(w.OtherBuyer);
        var otherSeller = await Token(w.OtherSeller);
        var buyer = await Token(w.Buyer);

        // Same role, not the owner, with a valid antiforgery token of their own.
        await Probe("other seller ships the order", HttpStatusCode.NotFound, Post(w.OtherSeller, "/Orders/Ship", new() { ["id"] = Id(w.OpenOrder) }, otherSeller));
        await Probe("other buyer receives the order", HttpStatusCode.NotFound, Post(w.OtherBuyer, "/Orders/Receive", new() { ["id"] = Id(w.OpenOrder) }, otherBuyer));
        await Probe("other buyer reads the order", HttpStatusCode.NotFound, w.OtherBuyer.GetAsync($"/Orders/Details/{w.OpenOrder}"));
        // B05 (CHECKOUT.md): an address that is not the buyer's own is an invalid choice (400); the page lists only their own addresses.
        using (var foreignAddress = await Post(w.OtherBuyer, "/Checkout/Place", new()
        {
            ["ProductId"] = Id(s.Product), ["AddressId"] = Id(w.XssAddress), ["Quantity"] = "1", ["ExpectedUnitPrice"] = "49.90", ["Key"] = Guid.NewGuid().ToString()
        }, otherBuyer))
        {
            if (foreignAddress.StatusCode != HttpStatusCode.BadRequest) mismatches.Add($"other buyer checks out with the buyer's address: {(int)foreignAddress.StatusCode}");
            Assert.DoesNotContain("xss-addr", await foreignAddress.Content.ReadAsStringAsync());
        }
        await Probe("other buyer opens the order confirmation", HttpStatusCode.NotFound, w.OtherBuyer.GetAsync($"/Checkout/Confirmation/{s.Order}"));
        await Probe("other buyer reviews the item", HttpStatusCode.NotFound, Post(w.OtherBuyer, "/Reviews/Create", new()
        {
            ["OrderItemId"] = Id(s.Item), ["Rating"] = "1", ["Comment"] = "not mine", ["Sentiment"] = "Negative"
        }, otherBuyer));
        await Probe("other seller replies to the feedback", HttpStatusCode.NotFound, Post(w.OtherSeller, "/Sellers/Reply", new() { ["feedbackId"] = Id(w.Feedback), ["reply"] = "not mine" }, otherSeller));
        await Probe("other buyer opens a request on the item", HttpStatusCode.NotFound, Post(w.OtherBuyer, "/Disputes/Open", new()
        {
            ["OrderItemId"] = Id(s.Item), ["Kind"] = "Return", ["Reason"] = "Damaged", ["Details"] = "not mine"
        }, otherBuyer));
        await Probe("other buyer saves a draft on the item", HttpStatusCode.NotFound, Post(w.OtherBuyer, "/Disputes/Draft", new()
        {
            ["OrderItemId"] = Id(s.Item), ["Kind"] = "Return", ["Details"] = "not mine"
        }, otherBuyer));
        foreach (var (who, client, token) in new[] { ("other buyer", w.OtherBuyer, otherBuyer), ("other seller", w.OtherSeller, otherSeller) })
        {
            await Probe($"{who} acts on the request", HttpStatusCode.NotFound, Post(client, "/Disputes/Act", new()
            {
                ["DisputeId"] = Id(w.Dispute), ["Action"] = "Message", ["Message"] = "not mine"
            }, token));
            await Probe($"{who} reads the request", HttpStatusCode.NotFound, client.GetAsync($"/Disputes/Details/{w.Dispute}"));
            var chat = await ChatToken(client);
            var path = $"/api/v1/chat/conversations/{w.Conversation}";
            await Probe($"{who} reads the conversation", HttpStatusCode.NotFound, client.GetAsync(path));
            await Probe($"{who} reads the history", HttpStatusCode.NotFound, client.GetAsync(path + "/messages"));
            await Probe($"{who} sends into the conversation", HttpStatusCode.NotFound, PostJson(client, path + "/messages", new ChatSendCommand(Guid.NewGuid(), "not mine"), chat));
            await Probe($"{who} moves the read cursor", HttpStatusCode.NotFound, PostJson(client, path + "/read", new { throughSequence = "1" }, chat));
            await Probe($"{who} opens the messages page on it", HttpStatusCode.NotFound, client.GetAsync($"/Messages?conversationId={w.Conversation}"));
        }
        await Probe("other buyer opens the buyer's account", HttpStatusCode.NotFound, w.OtherBuyer.GetAsync($"/Account/Details/{s.Buyer}"));
        // Wrong role entirely.
        await Probe("buyer moderates", HttpStatusCode.Forbidden, Post(w.Buyer, "/Admin/Moderate", new() { ["kind"] = "review", ["id"] = Id(w.Review), ["action"] = "Hide" }, buyer));
        await Probe("buyer decides a request", HttpStatusCode.Forbidden, Post(w.Buyer, "/Admin/DisputeAct", new()
        {
            ["DisputeId"] = Id(w.Dispute), ["Action"] = "Decide", ["ForBuyer"] = "true"
        }, buyer));
        await Probe("buyer ships an order", HttpStatusCode.Forbidden, Post(w.Buyer, "/Orders/Ship", new() { ["id"] = Id(w.OpenOrder) }, buyer));
        await Probe("seller opens a conversation as buyer", HttpStatusCode.Forbidden, PostJson(w.Seller, "/api/v1/chat/conversations", new { productId = s.OtherProduct }, await ChatToken(w.Seller)));
        // C02 (CHAT.md): Admin on a chat object is 404, like any non-member.
        await Probe("admin sends chat", HttpStatusCode.NotFound, PostJson(w.Admin, $"/api/v1/chat/conversations/{w.Conversation}/messages",
            new ChatSendCommand(Guid.NewGuid(), "admin"), Field(await w.Admin.GetStringAsync("/Account/Me"))));

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        Assert.Equal(before, Fingerprint(s.Target));
    }

    [Fact]
    public async Task Every_unsafe_endpoint_rejects_missing_and_foreign_antiforgery_tokens()
    {
        using var w = await Build();
        var s = w.S;
        // Owner, endpoint, valid form. JSON entries use the X-CSRF-TOKEN header (chat API).
        var forms = new (string Name, HttpClient Client, HttpClient Other, string Url, Dictionary<string, string>? Form, object? Json)[]
        {
            ("Account.Login", w.Anonymous, w.Buyer, "/Account/Login", new() { ["Email"] = "buyer@d01.example.invalid", ["Password"] = Password }, null),
            ("Account.Register", w.Anonymous, w.Buyer, "/Account/Register", new() { ["Name"] = "csrf", ["Email"] = "csrf@d01.example.invalid", ["Password"] = Password }, null),
            ("Admin.Moderate", w.Admin, w.Buyer, "/Admin/Moderate", new() { ["kind"] = "review", ["id"] = w.Review.ToString(), ["action"] = "Hide" }, null),
            ("Admin.DisputeAct", w.Admin, w.Buyer, "/Admin/DisputeAct", new() { ["DisputeId"] = w.Dispute.ToString(), ["Action"] = "Message", ["Message"] = "csrf" }, null),
            ("ChatApi.Open", w.Buyer, w.OtherBuyer, "/api/v1/chat/conversations", null, new { productId = s.OtherProduct }),
            ("ChatApi.Send", w.Buyer, w.OtherBuyer, $"/api/v1/chat/conversations/{w.Conversation}/messages", null, new ChatSendCommand(Guid.NewGuid(), "csrf")),
            ("ChatApi.Read", w.Seller, w.OtherSeller, $"/api/v1/chat/conversations/{w.Conversation}/read", null, new { throughSequence = "1" }),
            ("Checkout.AddAddress", w.Buyer, w.OtherBuyer, "/Checkout/AddAddress", new()
                { ["ProductId"] = s.Product.ToString(), ["Quantity"] = "1", ["FullName"] = "Csrf", ["Phone"] = "0123", ["Street"] = "S", ["City"] = "C", ["Country"] = "Vietnam" }, null),
            ("Checkout.Place", w.Buyer, w.OtherBuyer, "/Checkout/Place", new()
                { ["ProductId"] = s.Product.ToString(), ["AddressId"] = s.Address.ToString(), ["Quantity"] = "1", ["ExpectedUnitPrice"] = "49.90", ["Key"] = Guid.NewGuid().ToString() }, null),
            ("Disputes.Draft", w.Buyer, w.OtherBuyer, "/Disputes/Draft", new() { ["OrderItemId"] = OpenItem(w).ToString(), ["Kind"] = "ItemNotReceived", ["Details"] = "csrf" }, null),
            ("Disputes.Open", w.Buyer, w.OtherBuyer, "/Disputes/Open", new()
                { ["OrderItemId"] = OpenItem(w).ToString(), ["Kind"] = "ItemNotReceived", ["Preference"] = "Refund", ["Details"] = "csrf" }, null),
            ("Disputes.Act", w.Seller, w.OtherSeller, "/Disputes/Act", new() { ["DisputeId"] = w.Dispute.ToString(), ["Action"] = "Message", ["Message"] = "csrf" }, null),
            ("Orders.Ship", w.Seller, w.OtherSeller, "/Orders/Ship", new() { ["id"] = w.OpenOrder.ToString() }, null),
            ("Orders.Receive", w.Buyer, w.OtherBuyer, "/Orders/Receive", new() { ["id"] = w.OpenOrder.ToString() }, null),
            ("Reports.Create", w.OtherSeller, w.Seller, "/Reports/Create", new() { ["kind"] = "feedback", ["id"] = w.Feedback.ToString(), ["reason"] = "Spam" }, null),
            ("Reviews.Create", w.Buyer, w.OtherBuyer, "/Reviews/Create", new() { ["OrderItemId"] = OpenItem(w).ToString(), ["Rating"] = "5", ["Comment"] = "csrf" }, null),
            ("Sellers.Reply", w.Seller, w.OtherSeller, "/Sellers/Reply", new() { ["feedbackId"] = w.Feedback.ToString(), ["reply"] = "csrf" }, null),
            ("Account.Logout", w.Buyer, w.OtherBuyer, "/Account/Logout", new(), null),
        };
        // The sweep must name every unsafe endpoint of the inventory.
        var unsafeEndpoints = Inventory.Where(x => x.Contains(" POST ")).Select(x => x.Split(' ')[0]).ToHashSet();
        Assert.Equal(unsafeEndpoints.OrderBy(x => x), forms.Select(x => x.Name).OrderBy(x => x));

        Task<HttpResponseMessage> Send((string Name, HttpClient Client, HttpClient Other, string Url, Dictionary<string, string>? Form, object? Json) e, string? token) =>
            e.Json != null ? PostJson(e.Client, e.Url, e.Json, token) : Post(e.Client, e.Url, e.Form!, token);
        async Task<string> TokenOf(HttpClient client, bool json) =>
            json ? await ChatToken(client) : Field(await client.GetStringAsync(client == w.Anonymous ? "/Account/Login" : "/Account/Me"));

        var before = Fingerprint(s.Target);
        var failures = new List<string>();
        foreach (var e in forms)
        {
            var json = e.Json != null;
            // A different session's token (another signed-in user, or another browser for anonymous forms).
            var foreign = await TokenOf(e.Other, json);
            foreach (var (label, token) in new[] { ("no token", (string?)null), ("foreign token", foreign) })
            {
                using var response = await Send(e, token);
                var body = await response.Content.ReadAsStringAsync();
                // Antiforgery rejections are bare 400s; validation errors render a page, so the empty body tells them apart.
                if (response.StatusCode != HttpStatusCode.BadRequest || (!json && body.Length > 0))
                    failures.Add($"{e.Name} with {label}: {(int)response.StatusCode} ({body.Length} bytes)");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(before, Fingerprint(s.Target));
        Assert.Equal(HttpStatusCode.OK, (await w.Buyer.GetAsync("/Account/Me")).StatusCode); // Logout without a token did nothing.

        // Control: the same requests with the owner's own token get past antiforgery, so the 400s above were not bad forms.
        foreach (var e in forms)
        {
            using var response = await Send(e, await TokenOf(e.Client, e.Json != null));
            var body = await response.Content.ReadAsStringAsync();
            Assert.False(response.StatusCode == HttpStatusCode.BadRequest && body.Length == 0 && e.Json == null, $"{e.Name} control was rejected as CSRF.");
            Assert.False(response.StatusCode == HttpStatusCode.BadRequest && e.Json != null, $"{e.Name} control got 400.");
        }
    }

    private static int OpenItem(World w) => int.Parse(SqlFixture.Scalar(w.S.Target, $"SELECT id FROM dbo.OrderItem WHERE orderId={w.OpenOrder}"));

    [Fact]
    public async Task Stored_payloads_render_as_text_on_every_page_that_shows_them()
    {
        using var w = await Build();
        var s = w.S;
        var pages = new (string Who, HttpClient Client, string Url)[]
        {
            ("anonymous", w.Anonymous, $"/Catalog/Details/{s.Product}"), ("anonymous", w.Anonymous, $"/Catalog/Reviews/{s.Product}"),
            ("anonymous", w.Anonymous, $"/Sellers/Feedback/{s.Seller}"), ("anonymous", w.Anonymous, "/Catalog?q=" + Uri.EscapeDataString(Payload)),
            ("anonymous", w.Anonymous, "/Account/Login"), ("anonymous", w.Anonymous, "/Account/Register"), ("buyer", w.Buyer, $"/Checkout/AddAddress?productId={s.Product}"),
            ("buyer", w.Buyer, $"/Orders/Details/{s.Order}"), ("buyer", w.Buyer, "/Orders/Purchases"), ("buyer", w.Buyer, "/Disputes"),
            ("buyer", w.Buyer, $"/Disputes/Details/{w.Dispute}"), ("buyer", w.Buyer, "/Account/Me"), ("buyer", w.Buyer, $"/Checkout?productId={s.Product}"),
            ("buyer", w.Buyer, $"/Messages?conversationId={w.Conversation}"), ("buyer", w.Buyer, $"/Reviews/Create?orderItemId={s.Item}"),
            ("seller", w.Seller, $"/Orders/Details/{s.Order}"), ("seller", w.Seller, "/Orders/Sales"), ("seller", w.Seller, "/Disputes"),
            ("seller", w.Seller, $"/Disputes/Details/{w.Dispute}"), ("seller", w.Seller, $"/Sellers/Feedback/{s.Seller}"),
            ("seller", w.Seller, $"/Messages?conversationId={w.Conversation}"), ("seller", w.Seller, $"/Reports/Create?kind=review&id={w.Review}"),
            ("admin", w.Admin, "/Admin/Moderation"), ("admin", w.Admin, $"/Admin/ModerationItem?kind=review&id={w.Review}"),
            ("admin", w.Admin, $"/Admin/ModerationItem?kind=feedback&id={w.Feedback}"), ("admin", w.Admin, "/Admin/Disputes"),
            ("admin", w.Admin, $"/Admin/Dispute/{w.Dispute}"),
        };
        var seen = new Dictionary<string, List<string>>();
        var failures = new List<string>();
        foreach (var (who, client, url) in pages)
        {
            using var response = await client.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK) { failures.Add($"{who} {url}: {(int)response.StatusCode}"); continue; }
            foreach (var raw in RawSignatures) if (html.Contains(raw, StringComparison.OrdinalIgnoreCase)) failures.Add($"{who} {url}: raw '{raw}'");
            // The CSP allows no inline style or script; F04's journeys found star widths set by style attributes.
            foreach (var inline in new[] { " style=\"", "<script>", "<style" }) if (html.Contains(inline, StringComparison.OrdinalIgnoreCase)) failures.Add($"{who} {url}: inline '{inline.Trim()}' near '{Around(html, inline)}'");
            foreach (var marker in System.Text.RegularExpressions.Regex.Matches(html, @"xss-[a-z-]+").Select(m => m.Value).Distinct())
                (seen.TryGetValue(marker, out var list) ? list : seen[marker] = []).Add($"{who} {url}");
        }
        // The JSON API escapes markup characters; the client renders with textContent (C04).
        foreach (var url in new[] { "/api/v1/chat/conversations", $"/api/v1/chat/conversations/{w.Conversation}/messages" })
        {
            var json = await w.Seller.GetStringAsync(url);
            Assert.Contains("xss-", json);
            foreach (var raw in RawSignatures) Assert.DoesNotContain(raw, json);
            Assert.Contains("\\u003Cimg src=x onerror", json);
            Assert.Contains("application/json", (await w.Seller.GetAsync(url)).Content.Headers.ContentType!.ToString());
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // Each injected field really reached an HTML page (otherwise "no raw payload" would prove nothing).
        foreach (var field in new[] { "name", "addr-name", "addr-street", "review", "reply", "dispute", "dispute-msg", "report", "note" })
            Assert.True(seen.ContainsKey("xss-" + field), $"xss-{field} never rendered; pages checked: {string.Join(", ", pages.Select(p => p.Url))}");
        Assert.Contains($"anonymous /Catalog/Details/{s.Product}", seen["xss-review"]);
        Assert.Contains($"seller /Disputes/Details/{w.Dispute}", seen["xss-dispute"]);
        Assert.Contains($"admin /Admin/ModerationItem?kind=review&id={w.Review}", seen["xss-report"]);
    }

    [Fact]
    public async Task Hardening_headers_are_on_pages_api_static_files_and_errors()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        // shop.example stands in for a real domain: HSTS applies there, never to localhost.
        using var factory = new AccountHttpTests.Factory(target, settings: new Dictionary<string, string> { ["AllowedHosts"] = "localhost;shop.example" });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        foreach (var url in new[] { "/Account/Login", "/api/v1/chat/conversations", "/css/site.css", "/no-such-page", "/Catalog/Details/2147483647", "/health/live" })
        {
            using var response = await client.GetAsync(url);
            string Get(string name) => response.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : "";
            Assert.True(Get("Content-Security-Policy") == SecurityHeaders.ContentSecurityPolicy, $"{url} CSP: {Get("Content-Security-Policy")}");
            Assert.Equal("nosniff", Get("X-Content-Type-Options"));
            Assert.Equal("DENY", Get("X-Frame-Options"));
            Assert.Equal("strict-origin-when-cross-origin", Get("Referrer-Policy"));
            Assert.Equal("same-origin", Get("Cross-Origin-Opener-Policy"));
            Assert.Equal("", Get("Strict-Transport-Security"));
            Assert.Equal("", Get("X-Powered-By"));
            Assert.False(response.Headers.Contains("Server") && Get("Server").Contains("Kestrel"), $"{url} reveals Kestrel");
        }
        Assert.Contains("frame-ancestors 'none'", SecurityHeaders.ContentSecurityPolicy);
        Assert.DoesNotContain("unsafe", SecurityHeaders.ContentSecurityPolicy);
        using var domain = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://shop.example") });
        var hsts = await domain.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, hsts.StatusCode);
        Assert.Matches("^max-age=\\d+", string.Join(",", hsts.Headers.GetValues("Strict-Transport-Security")));
        using var plain = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://shop.example") });
        Assert.False((await plain.GetAsync("/Account/Login")).Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Auth_and_input_edges_leak_nothing_and_redirect_only_locally()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        async Task<HttpResponseMessage> SignIn(string email, string password, string? returnUrl = null)
        {
            var form = new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Field(await anonymous.GetStringAsync("/Account/Login")) };
            if (returnUrl != null) form["ReturnUrl"] = returnUrl;
            return await anonymous.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
        }
        // Wrong password and unknown email are indistinguishable (status and message).
        var wrong = await SignIn("buyer@d01.example.invalid", "Wrong-password-123");
        var unknown = await SignIn("nobody@d01.example.invalid", "Wrong-password-123");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        string Errors(string html) => string.Join("|", System.Text.RegularExpressions.Regex.Matches(html, "<li>([^<]*)</li>").Select(m => m.Groups[1].Value));
        var wrongErrors = Errors(WebUtilityDecode(await wrong.Content.ReadAsStringAsync()));
        Assert.Contains("Unable to sign in", wrongErrors);
        Assert.Equal(wrongErrors, Errors(WebUtilityDecode(await unknown.Content.ReadAsStringAsync())));

        // Open redirect: only local return URLs are honoured.
        foreach (var evil in new[] { "https://evil.example/", "//evil.example/", "/\\evil.example/", "https:evil.example" })
        {
            using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            var form = new Dictionary<string, string> { ["Email"] = "buyer@d01.example.invalid", ["Password"] = Password, ["ReturnUrl"] = evil,
                ["__RequestVerificationToken"] = Field(await client.GetStringAsync("/Account/Login")) };
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Me", response.Headers.Location!.OriginalString);
        }

        // A tampered auth cookie is just an anonymous visitor.
        using var forged = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        forged.DefaultRequestHeaders.Add("Cookie", "EbayGroup5.Auth=CfDJ8" + new string('A', 300));
        var forgedMe = await forged.GetAsync("/Account/Me");
        Assert.Equal(HttpStatusCode.Redirect, forgedMe.StatusCode);
        Assert.Contains("/Account/Login", forgedMe.Headers.Location!.OriginalString);

        // Injection-shaped input stays data; malformed JSON gets a generic problem without exception details.
        var search = await anonymous.GetAsync("/Catalog?q=" + Uri.EscapeDataString("' OR 1=1 --"));
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Contains("No exact matches found", WebUtilityDecode(await search.Content.ReadAsStringAsync()));
        using var buyer = await Login(factory, "buyer");
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/conversations/1/messages") { Content = new StringContent("{\"content\":", System.Text.Encoding.UTF8, "application/json") };
        malformed.Headers.Add(Header, await ChatToken(buyer));
        var bad = await buyer.SendAsync(malformed);
        var problem = await bad.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        foreach (var leak in new[] { "Exception", "at EbayGroup5", "System.Text.Json", "LineNumber", "Path:" }) Assert.DoesNotContain(leak, problem);

        // Brute force: the eleventh sign-in in a window is refused even with the right password (shared window, B02/F02).
        // Windows are fixed and clock-aligned, so a run that straddles a boundary restarts the count: use a long window and,
        // if a boundary still falls inside the run, repeat once on a fresh host (own Redis prefix).
        List<HttpStatusCode> statuses = [];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var fresh = new AccountHttpTests.Factory(s.Target, settings: new Dictionary<string, string> { ["RateLimits:auth:WindowSeconds"] = "3600" });
            using var guesser = fresh.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
            statuses = [];
            for (var i = 0; i < 12; i++)
            {
                var form = new Dictionary<string, string> { ["Email"] = "otherbuyer@d01.example.invalid", ["Password"] = i < 11 ? "Wrong-password-123" : Password,
                    ["__RequestVerificationToken"] = Field(await guesser.GetStringAsync("/Account/Login")) };
                statuses.Add((await guesser.PostAsync("/Account/Login", new FormUrlEncodedContent(form))).StatusCode);
            }
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 == window) break;
        }
        Assert.All(statuses.Take(10), x => Assert.Equal(HttpStatusCode.Unauthorized, x));
        Assert.All(statuses.Skip(10), x => Assert.Equal(HttpStatusCode.TooManyRequests, x));
    }

    private static string Around(string text, string part)
    {
        var at = text.IndexOf(part, StringComparison.OrdinalIgnoreCase);
        return text.Substring(Math.Max(0, at - 120), Math.Min(240, text.Length - Math.Max(0, at - 120)));
    }

    private static string WebUtilityDecode(string html) => WebUtility.HtmlDecode(html);
}
