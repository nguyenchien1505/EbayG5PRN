// F03 browser journeys (docs/architecture/JOURNEYS.md) on the Docker stack: Nginx HTTPS, two web replicas, seeded demo data.
// Usage: EbayGroup5.Journeys --base https://localhost:N --spki <base64 SHA-256 of the server SPKI> --out <dir> [--screens <dir>]
// Passwords come from EBAY_JOURNEY_{BUYER,SELLER,ADMIN}_PASSWORD (never arguments). Writes chat markers to <out>/markers.txt.
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
var baseUrl = options["--base"].TrimEnd('/');
var output = Directory.CreateDirectory(options["--out"]).FullName;
string Secret(string role) => Environment.GetEnvironmentVariable($"EBAY_JOURNEY_{role}_PASSWORD") ?? throw new InvalidOperationException($"EBAY_JOURNEY_{role}_PASSWORD is required.");
var run = Guid.NewGuid().ToString("N")[..10];
SetDefaultExpectTimeout(15_000);

using var playwright = await Playwright.CreateAsync();
// Chrome accepts only the throw-away server key of this stack (no blanket "ignore HTTPS errors").
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Channel = "chrome", Headless = true, Args = [$"--ignore-certificate-errors-spki-list={options["--spki"]}"]
});
var pages = new List<(string Name, IPage Page)>();
// F04 (SECURITY_TESTS.md): every page runs under the real CSP; Chrome reports each violation on the console. Any violation or
// JavaScript dialog (an executed payload) fails the run.
var violations = new System.Collections.Concurrent.ConcurrentBag<string>();
async Task<IPage> NewPage(string name)
{
    var context = await browser.NewContextAsync(new() { BaseURL = baseUrl, Locale = "en-US" });
    context.SetDefaultTimeout(20_000);
    var page = await context.NewPageAsync();
    page.Console += (_, message) =>
    {
        if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)) violations.Add($"{name} {page.Url}: {message.Text}");
    };
    page.Dialog += async (_, dialog) => { violations.Add($"{name} {page.Url}: dialog '{dialog.Message}'"); await dialog.DismissAsync(); };
    pages.Add((name, page));
    return page;
}
async Task<IPage> SignIn(string name, string email, string password)
{
    var page = await NewPage(name);
    for (var attempt = 0; attempt < 3; attempt++)
    {
        await page.GotoAsync("/Account/Login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        var response = await page.RunAndWaitForResponseAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync(),
            r => r.Url.Contains("/Account/Login") && r.Request.Method == "POST");
        if (response.Status != 429) break;
        // The shared auth limit (F02) counts this host's address; wait for the next window instead of failing.
        var wait = int.TryParse(await response.HeaderValueAsync("retry-after"), out var seconds) ? seconds : 60;
        Console.WriteLine($"INFO auth limit reached; waiting {wait}s");
        await Task.Delay(TimeSpan.FromSeconds(wait + 1));
    }
    await Expect(page).ToHaveURLAsync(new Regex("/Account/Me"));
    return page;
}
var failures = 0;
async Task Step(string name, Func<Task> body)
{
    if (failures > 0) { Console.WriteLine("SKIP " + name); return; }
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex)
    {
        failures++;
        // Playwright puts the useful detail (strict mode, call log) after the first line.
        var detail = string.Join(" | ", ex.Message.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(4));
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {detail}");
        foreach (var (role, page) in pages)
            try { await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"fail-{role}.png"), FullPage = true }); } catch { }
    }
}

// G01 (docs/architecture/UI_ACCEPTANCE.md): --screens <dir> builds clean demo data through the UI (no test markup) and captures every
// group 5 screen and entry point at desktop 1280x900 and mobile 390x844 instead of running J1-J7. Overflow and CSP problems are listed.
if (options.TryGetValue("--screens", out var screensDir)) return await CaptureScreens(Directory.CreateDirectory(screensDir).FullName);

async Task<int> CaptureScreens(string dir)
{
    var problems = new List<string>();
    var shots = new List<string>();
    async Task Shot(IPage page, string id, string name, bool fullPage = true)
    {
        foreach (var (label, width, height) in new[] { ("desktop", 1280, 900), ("mobile", 390, 844) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.WaitForTimeoutAsync(400);
            if (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"))
                problems.Add($"{id} {label}: horizontal overflow");
            var file = $"{id}-{name}-{label}.jpg";
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, file), FullPage = fullPage, Type = ScreenshotType.Jpeg, Quality = 70 });
            shots.Add(file);
        }
        await page.SetViewportSizeAsync(1280, 900);
    }
    async Task<(string Delivered, string Shipped)> SeedOrders(IPage page)
    {
        await page.GotoAsync("/Orders/Purchases");
        var links = await page.Locator("a[href*='/Orders/Details/']").EvaluateAllAsync<string[]>("els => [...new Set(els.map(e => e.getAttribute('href')))]");
        string delivered = "", shipped = "";
        foreach (var link in links)
        {
            await page.GotoAsync(link);
            var status = (await page.Locator(".order-status").InnerTextAsync()).Trim();
            if (status == "Delivered" && delivered == "") delivered = link;
            if (status == "Shipped" && shipped == "") shipped = link;
        }
        if (delivered == "" || shipped == "") throw new InvalidOperationException("Seeded delivered/shipped orders not found.");
        return (delivered, shipped);
    }

    try
    {
        var b = await SignIn("buyer", "buyer.a06@example.invalid", Secret("BUYER"));
        var s = await SignIn("seller", "seller.a06@example.invalid", Secret("SELLER"));
        var a = await SignIn("admin", "admin.a06@example.invalid", Secret("ADMIN"));

        // Catalog, item page and the chat panel (U1); a short buyer/seller exchange for the Messages screens.
        await b.GotoAsync("/Catalog");
        await b.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
        var product = b.Url;
        await b.Locator("button.seller-message").ClickAsync();
        var panel = b.Locator("#chat-panel");
        await Expect(panel.Locator("textarea[data-input]")).ToBeEnabledAsync();
        await panel.Locator("textarea[data-input]").FillAsync("Hi, are spare ear cushions included?");
        await panel.Locator("button.chat-send").ClickAsync();
        await Expect(panel.Locator("[data-message-list]").GetByText("Hi, are spare ear cushions included?")).ToBeVisibleAsync();
        // The panel is fixed to the viewport, so this one is a viewport capture.
        await Shot(b, "S03", "item-chat-panel", fullPage: false);
        await s.GotoAsync("/Messages");
        await s.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
        await s.Locator("#chat-app textarea[data-input]").FillAsync("Yes, one spare pair comes in the box.");
        await s.Locator("#chat-app button.chat-send").ClickAsync();
        await Expect(s.Locator("[data-message-list]").GetByText("Yes, one spare pair comes in the box.")).ToBeVisibleAsync();

        // Seeded orders: one delivered (two lines), one shipped. Feedback on line 1, return on line 2, item not received on the shipped one.
        var (delivered, shipped) = await SeedOrders(b);
        await b.GotoAsync("/Orders/Purchases");
        await Shot(b, "S06", "purchases");
        await b.GotoAsync(delivered);
        await Shot(b, "S07", "order-details");
        var lines = await b.Locator("[id^=line-]").EvaluateAllAsync<string[]>("els => els.map(e => e.id)");
        await b.Locator("#" + lines[0]).GetByRole(AriaRole.Link, new() { Name = "Leave feedback" }).ClickAsync();
        await b.Locator("label[for=sentiment-Positive]").ClickAsync();
        await b.Locator("label[for=rating-5]").ClickAsync();
        await b.Locator("#feedback-comment").FillAsync("Great sound and comfortable for long calls. Fast shipping too.");
        await b.Locator("label[for=arrived-yes]").ClickAsync();
        foreach (var detail in new[] { "DescriptionRating-5", "ShippingTimeRating-4", "CommunicationRating-5" }) await b.Locator($"label[for={detail}]").ClickAsync();
        await Shot(b, "S08", "leave-feedback");
        await b.GetByRole(AriaRole.Button, new() { Name = "Leave feedback" }).ClickAsync();
        await Expect(b.GetByText("You rated this item 5 out of 5.")).ToBeVisibleAsync();

        await b.GotoAsync(delivered);
        await b.Locator("#" + lines[1]).GetByRole(AriaRole.Link, new() { Name = "Return this item" }).ClickAsync();
        await Shot(b, "S09", "return-reason");
        await b.GetByText("Item damaged", new() { Exact = true }).ClickAsync();
        await b.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await b.Locator("textarea[name=Details]").FillAsync("The connector is loose and the cable stops charging.");
        await Shot(b, "S10", "return-details");
        await b.GetByRole(AriaRole.Button, new() { Name = "Confirm return" }).ClickAsync();
        await Expect(b.Locator(".account-notice")).ToContainTextAsync("Your request was sent.");

        await b.GotoAsync(shipped);
        await b.Locator("[id^=line-]").First.GetByRole(AriaRole.Link, new() { Name = "I didn't receive it" }).ClickAsync();
        await b.GetByText("I'd like a refund", new() { Exact = true }).ClickAsync();
        await b.Locator("#dispute-details").FillAsync("Tracking has not updated for a week.");
        await Shot(b, "S11", "item-not-received");
        await b.GetByRole(AriaRole.Button, new() { Name = "Send request" }).ClickAsync();
        await Expect(b.Locator(".account-notice")).ToContainTextAsync("Your request was sent.");

        // Catalog card and item page after the feedback: stars, rating count and seller % positive (entry points A1/A2).
        await b.GotoAsync("/Catalog");
        await Shot(b, "S01", "catalog");
        await b.GotoAsync(product);
        await Shot(b, "S02", "item");

        // Requests, reviews, feedback profile, Messages.
        await b.GotoAsync("/Disputes");
        await Shot(b, "S12", "returns-and-requests");
        await b.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^See (request )?details$") }).First.ClickAsync();
        await Shot(b, "S13", "request-buyer");
        await b.GotoAsync(product);
        var reviews = await b.Locator("a[href*='/Catalog/Reviews/']").First.GetAttributeAsync("href");
        await b.GotoAsync(reviews!);
        await Shot(b, "S04", "item-reviews");
        await b.GotoAsync(product);
        var profile = await b.Locator("a.seller-feedback-link").First.GetAttributeAsync("href");
        await b.GotoAsync(profile!);
        await Shot(b, "S05", "seller-feedback");
        await b.GotoAsync("/Messages");
        await b.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
        await Expect(b.Locator("[data-message-list]").GetByText("Yes, one spare pair comes in the box.")).ToBeVisibleAsync();
        await Shot(b, "S14", "messages-buyer");

        await s.GotoAsync("/Messages");
        await s.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
        await Shot(s, "S15", "messages-seller");
        await s.GotoAsync("/Disputes?kind=Return");
        await s.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^See (request )?details$") }).First.ClickAsync();
        await Shot(s, "S16", "request-seller");
        await s.GotoAsync(profile!);
        await Shot(s, "S17", "seller-feedback-owner");

        await a.GotoAsync("/Admin/Moderation");
        await Shot(a, "S18", "admin-moderation");
        await a.GotoAsync("/Admin/Disputes");
        await Shot(a, "S19", "admin-disputes");
    }
    catch (Exception ex)
    {
        var detail = string.Join(" | ", ex.Message.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(4));
        Console.WriteLine($"FAIL screens: {ex.GetType().Name}: {detail}");
        foreach (var (role, page) in pages)
            try { await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"fail-{role}.png"), FullPage = true }); } catch { }
        return 1;
    }
    problems.AddRange(violations);
    await File.WriteAllLinesAsync(Path.Combine(dir, "screens.txt"), shots.Concat(problems.Select(p => "PROBLEM " + p)));
    Console.WriteLine($"Screens: {shots.Count} files in {dir}; {problems.Count} problem(s)");
    foreach (var problem in problems) Console.WriteLine("PROBLEM " + problem);
    return 0;
}

IPage buyer = null!, seller = null!, admin = null!;
int order = 0;
var buyerMessage = $"journey buyer note {run}";
var sellerMessage = $"journey seller reply {run}";
// F04: user text carries markup that must stay text (the page shows it literally, nothing executes).
const string Payload = "<img src=x onerror=\"window.__xss=1\"><script>window.__xss=1</script>";
var reviewComment = $"Journey review {run}: arrived quickly. {Payload}";

await Step("J1 sign in as demo buyer, seller and admin", async () =>
{
    buyer = await SignIn("buyer", "buyer.a06@example.invalid", Secret("BUYER"));
    seller = await SignIn("seller", "seller.a06@example.invalid", Secret("SELLER"));
    admin = await SignIn("admin", "admin.a06@example.invalid", Secret("ADMIN"));
});

await Step("J1 buyer finds an item and places a COD order", async () =>
{
    await buyer.GotoAsync("/");
    await buyer.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
    await Expect(buyer.GetByRole(AriaRole.Heading, new() { Name = "Wireless Headphones" })).ToBeVisibleAsync();
    await buyer.GetByRole(AriaRole.Link, new() { Name = "Buy It Now" }).ClickAsync();
    await buyer.Locator("input[name=AddressId]").First.CheckAsync();
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();
    await Expect(buyer.GetByRole(AriaRole.Heading, new() { Name = "Your order is placed" })).ToBeVisibleAsync();
    order = int.Parse(Regex.Match(await buyer.Locator(".confirmation-card").InnerTextAsync(), @"Order #(\d+)").Groups[1].Value);
    await buyer.GotoAsync("/Orders/Purchases");
    await Expect(buyer.GetByText($"Order #{order}")).ToBeVisibleAsync();
});

await Step("J2 buyer messages the seller and both see replies live over WSS", async () =>
{
    await buyer.GotoAsync("/");
    await buyer.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
    await buyer.Locator("button.seller-message").ClickAsync();
    var panel = buyer.Locator("#chat-panel");
    await Expect(panel).ToBeVisibleAsync();
    var input = panel.Locator("textarea[data-input]");
    await Expect(input).ToBeEnabledAsync();
    await input.FillAsync(buyerMessage);
    await panel.Locator("button.chat-send").ClickAsync();
    await Expect(panel.Locator("[data-message-list]").GetByText(buyerMessage)).ToBeVisibleAsync();

    await seller.GotoAsync("/Messages");
    await seller.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
    await Expect(seller.Locator("[data-message-list]").GetByText(buyerMessage)).ToBeVisibleAsync();
    var reply = seller.Locator("#chat-app textarea[data-input]");
    await Expect(reply).ToBeEnabledAsync();
    await reply.FillAsync(sellerMessage);
    await seller.Locator("#chat-app button.chat-send").ClickAsync();
    // The buyer's open panel receives the reply without reloading (Redis backplane across replicas).
    await Expect(panel.Locator("[data-message-list]").GetByText(sellerMessage)).ToBeVisibleAsync();
    await File.WriteAllLinesAsync(Path.Combine(output, "markers.txt"), [buyerMessage, sellerMessage]);
});

await Step("J3 seller ships, buyer confirms receipt and leaves feedback", async () =>
{
    await seller.GotoAsync($"/Orders/Details/{order}");
    await seller.GetByRole(AriaRole.Button, new() { Name = "Mark as shipped" }).ClickAsync();
    await Expect(seller.Locator(".order-status")).ToHaveTextAsync("Shipped");
    await buyer.GotoAsync($"/Orders/Details/{order}");
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Confirm received" }).ClickAsync();
    await Expect(buyer.Locator(".order-status")).ToHaveTextAsync("Delivered");
    await buyer.Locator("#line-" + await LineId(buyer)).GetByRole(AriaRole.Link, new() { Name = "Leave feedback" }).ClickAsync();
    await buyer.Locator("label[for=sentiment-Positive]").ClickAsync();
    await buyer.Locator("label[for=rating-5]").ClickAsync();
    await buyer.Locator("#feedback-comment").FillAsync(reviewComment);
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Leave feedback" }).ClickAsync();
    await Expect(buyer.GetByText("You rated this item 5 out of 5.")).ToBeVisibleAsync();
    await buyer.GotoAsync("/");
    await buyer.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
    await Expect(buyer.GetByText(reviewComment)).ToBeVisibleAsync();
});

await Step("J4 return: buyer opens it, seller accepts, buyer ships back, seller refunds", async () =>
{
    await buyer.GotoAsync($"/Orders/Details/{order}");
    await buyer.Locator("#line-" + await LineId(buyer)).GetByRole(AriaRole.Link, new() { Name = "Return this item" }).ClickAsync();
    await Expect(buyer.Locator("[data-next]")).ToBeVisibleAsync();
    await buyer.GetByText("Item damaged", new() { Exact = true }).ClickAsync();
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
    await buyer.Locator("textarea[name=Details]").FillAsync("The headband cracked in transit. " + Payload);
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Confirm return" }).ClickAsync();
    await Expect(buyer.Locator(".account-notice")).ToContainTextAsync("Your request was sent.");

    await seller.GotoAsync("/Disputes?kind=Return");
    await seller.GetByRole(AriaRole.Link, new() { Name = "See details" }).First.ClickAsync();
    await Expect(seller.GetByRole(AriaRole.Heading, new() { Name = "You have a return request" })).ToBeVisibleAsync();
    await seller.GetByText("Accept the return", new() { Exact = true }).ClickAsync();
    await seller.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
    await seller.GetByRole(AriaRole.Button, new() { Name = "Accept return" }).ClickAsync();
    await Expect(seller.Locator(".account-notice")).ToHaveTextAsync("You accepted the return.");
    var request = seller.Url;

    await buyer.GotoAsync(request);
    await buyer.GetByText("I shipped the item", new() { Exact = true }).ClickAsync();
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
    await buyer.GetByLabel("Tracking number").FillAsync("JOURNEY-" + run);
    await buyer.GetByRole(AriaRole.Button, new() { Name = "Confirm shipment" }).ClickAsync();
    await Expect(buyer.Locator(".account-notice")).ToHaveTextAsync("Your return shipment was recorded.");

    await seller.GotoAsync(request);
    await seller.GetByText("Refund the buyer", new() { Exact = true }).ClickAsync();
    await seller.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
    await seller.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Refund US \\$") }).ClickAsync();
    await buyer.GotoAsync(request);
    await Expect(buyer.Locator(".dispute-headline")).ToHaveTextAsync("Closed — refunded");
    await Expect(buyer.GetByText("eBay sent an automatic update.").First).ToBeVisibleAsync();
});

await Step("J5 seller reports the review and an admin hides it", async () =>
{
    await seller.GotoAsync("/");
    await seller.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
    var review = seller.Locator("li.review-item").Filter(new() { HasText = reviewComment });
    await review.GetByRole(AriaRole.Link, new() { Name = "Report" }).ClickAsync();
    await seller.Locator("input[name=reason]").First.CheckAsync();
    await seller.GetByRole(AriaRole.Button, new() { Name = "Send report" }).ClickAsync();
    await Expect(seller.Locator(".account-notice")).ToHaveTextAsync("Thanks — we'll review your report.");

    await admin.GotoAsync("/Admin/Moderation");
    await admin.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Review #") }).First.ClickAsync();
    await Expect(admin.GetByText(reviewComment)).ToBeVisibleAsync();
    await admin.GetByRole(AriaRole.Button, new() { Name = "Hide content" }).ClickAsync();
    await Expect(admin.Locator(".account-notice")).ToHaveTextAsync("Hide recorded in the moderation audit.");
    await buyer.GotoAsync("/");
    await buyer.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
    await Expect(buyer.GetByText(reviewComment)).ToHaveCountAsync(0);
});

await Step("J6 access control: login redirect, role 403, foreign request 404", async () =>
{
    var anonymous = await NewPage("anonymous");
    await anonymous.GotoAsync("/Orders/Purchases");
    await Expect(anonymous).ToHaveURLAsync(new Regex("/Account/Login\\?ReturnUrl="));
    var forbidden = await buyer.GotoAsync("/Admin/Moderation");
    if (forbidden?.Status != 403) throw new InvalidOperationException($"Buyer on /Admin/Moderation got {forbidden?.Status}.");
    await Expect(buyer.GetByRole(AriaRole.Heading, new() { Name = "You don't have access to this page." })).ToBeVisibleAsync();
    var sellerOnBuyerPage = await seller.GotoAsync("/Orders/Purchases");
    if (sellerOnBuyerPage?.Status != 403) throw new InvalidOperationException($"Seller on /Orders/Purchases got {sellerOnBuyerPage?.Status}.");
    var missing = await buyer.GotoAsync("/Disputes/Details/2147483647");
    if (missing?.Status != 404) throw new InvalidOperationException($"Missing request got {missing?.Status}.");
    var adminOnChat = await admin.GotoAsync("/Messages");
    if (adminOnChat?.Status != 403) throw new InvalidOperationException($"Admin on /Messages got {adminOnChat?.Status}.");
});

await Step("J7 stored markup in chat, review and return stays text; no CSP violation or dialog anywhere (J1-J7)", async () =>
{
    var chatPayload = $"journey xss {run} {Payload}";
    await buyer.GotoAsync("/Messages");
    await buyer.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
    var input = buyer.Locator("#chat-app textarea[data-input]");
    await Expect(input).ToBeEnabledAsync();
    await input.FillAsync(chatPayload);
    await buyer.Locator("#chat-app button.chat-send").ClickAsync();
    await Expect(buyer.Locator("[data-message-list]").GetByText(chatPayload)).ToBeVisibleAsync();
    await seller.GotoAsync("/Messages");
    // Inbox preview (client-rendered) and the open conversation show the markup literally.
    await Expect(seller.Locator("[data-conversations]").GetByText($"journey xss {run}").First).ToBeVisibleAsync();
    await seller.Locator("[data-conversations]").GetByText("Wireless Headphones").First.ClickAsync();
    await Expect(seller.Locator("[data-message-list]").GetByText(chatPayload)).ToBeVisibleAsync();
    // The return details from J4 (server-rendered) show the markup literally; the J3 review was checked the same way before J5 hid it.
    await seller.GotoAsync("/Disputes?kind=Return");
    await seller.GetByRole(AriaRole.Link, new() { Name = "See details" }).First.ClickAsync();
    await Expect(seller.GetByText("The headband cracked in transit. " + Payload)).ToBeVisibleAsync();
    foreach (var (role, page) in pages)
        if (!await page.EvaluateAsync<bool>("() => window.__xss === undefined")) throw new InvalidOperationException($"Payload executed on {role} {page.Url}.");
    if (!violations.IsEmpty) throw new InvalidOperationException("CSP violations or dialogs: " + string.Join(" | ", violations.Take(5)));
});

if (options.TryGetValue("--demo-clock", out var demoClock) && int.TryParse(demoClock, out var demoSeconds) && demoSeconds > 0)
{
    await Step("G02 new-order INR draft, real worker escalation and admin decision with demo clock", async () =>
    {
        await buyer.GotoAsync("/Catalog");
        await buyer.GetByRole(AriaRole.Link, new() { Name = "Wireless Headphones" }).First.ClickAsync();
        await buyer.GetByRole(AriaRole.Link, new() { Name = "Buy It Now" }).ClickAsync();
        await buyer.Locator("input[name=AddressId]").First.CheckAsync();
        await buyer.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();
        await Expect(buyer.GetByRole(AriaRole.Heading, new() { Name = "Your order is placed" })).ToBeVisibleAsync();
        var freshOrder = int.Parse(Regex.Match(await buyer.Locator(".confirmation-card").InnerTextAsync(), @"Order #(\d+)").Groups[1].Value);
        // This is a fresh transaction; do not edit the seeded dates or database state.
        await Task.Delay(TimeSpan.FromSeconds(5 * demoSeconds + 2));
        await buyer.GotoAsync($"/Orders/Details/{freshOrder}");
        await buyer.Locator("#line-" + await LineId(buyer)).GetByRole(AriaRole.Link, new() { Name = "I didn't receive it" }).ClickAsync();
        var details = $"G02 missing parcel {run}";
        await buyer.Locator("textarea[name=Details]").FillAsync(details);
        await Expect(buyer.Locator("[data-draft-status]")).ToHaveTextAsync("Draft saved");
        await buyer.ReloadAsync();
        await Expect(buyer.Locator("textarea[name=Details]")).ToHaveValueAsync(details);
        await buyer.GetByText("I'd like a refund", new() { Exact = true }).ClickAsync();
        await buyer.GetByRole(AriaRole.Button, new() { Name = "Send request" }).ClickAsync();
        await Expect(buyer.Locator(".account-notice")).ToContainTextAsync("Your request was sent.");
        await buyer.Locator("a[href*='/Disputes/Details/']").First.ClickAsync();
        await Expect(buyer).ToHaveURLAsync(new Regex(@"/Disputes/Details/\d+"));
        var requestUrl = buyer.Url;
        var requestId = int.Parse(Regex.Match(requestUrl, @"/Disputes/Details/(\d+)").Groups[1].Value);
        // Seller intentionally does not respond; wait for the actual automation worker to escalate.
        var deadline = DateTime.UtcNow.AddSeconds(3 * demoSeconds + 75);
        var escalated = false;
        do
        {
            await admin.GotoAsync($"/Admin/Dispute/{requestId}");
            escalated = await admin.GetByRole(AriaRole.Button, new() { Name = "Confirm decision" }).IsVisibleAsync();
            if (!escalated) await Task.Delay(2000);
        } while (!escalated && DateTime.UtcNow < deadline);
        if (!escalated) throw new InvalidOperationException("Demo request was not escalated by the worker.");
        await admin.Locator("input[name=ForBuyer][value=true]").CheckAsync();
        await admin.Locator("#decision-note").FillAsync("G02 demo decision: buyer protected, refund simulated.");
        await admin.GetByRole(AriaRole.Button, new() { Name = "Confirm decision" }).ClickAsync();
        await buyer.GotoAsync(requestUrl);
        await Expect(buyer.Locator(".dispute-headline")).ToHaveTextAsync("Closed — decided in your favor");
        await seller.GotoAsync(requestUrl);
        await Expect(seller.Locator(".dispute-headline")).ToHaveTextAsync("Closed — decided in the buyer's favor");
        if (!violations.IsEmpty) throw new InvalidOperationException("Demo rehearsal reported a CSP violation or dialog.");
        await File.WriteAllTextAsync(Path.Combine(output, "demo-rehearsal.json"), System.Text.Json.JsonSerializer.Serialize(new
            { demoSeconds, freshOrder, requestId, draftReload = true, workerEscalation = true, adminDecision = true, utc = DateTime.UtcNow }));
    });
}
Console.WriteLine(failures == 0 ? "PASS: browser journeys" : $"FAIL: {failures} browser journey step(s)");
return failures == 0 ? 0 : 1;

// The first order line of the current order page (journeys buy one item per order).
static async Task<string> LineId(IPage page) =>
    (await page.Locator("[id^=line-]").First.GetAttributeAsync("id"))!["line-".Length..];
