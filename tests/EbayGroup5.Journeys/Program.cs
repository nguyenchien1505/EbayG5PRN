// F03 browser journeys (docs/architecture/JOURNEYS.md) on the Docker stack: Nginx HTTPS, two web replicas, seeded demo data.
// Usage: EbayGroup5.Journeys --base https://localhost:N --spki <base64 SHA-256 of the server SPKI> --out <dir>
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
    await buyer.Locator("#review-comment").FillAsync(reviewComment);
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

Console.WriteLine(failures == 0 ? "PASS: browser journeys" : $"FAIL: {failures} browser journey step(s)");
return failures == 0 ? 0 : 1;

// The first order line of the current order page (journeys buy one item per order).
static async Task<string> LineId(IPage page) =>
    (await page.Locator("[id^=line-]").First.GetAttributeAsync("id"))!["line-".Length..];
