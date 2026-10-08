using System.Net;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>F03: one system-wide access matrix (route x role x foreign object), as recorded in JOURNEYS.md.</summary>
public sealed class AccessMatrixTests
{
    private const string Login = "login", Unauthorized = "401", Ok = "200", Forbidden = "403", NotFound = "404";

    private static async Task<string> Outcome(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("/Account/Login") == true) return Login;
        return ((int)response.StatusCode).ToString();
    }

    [Fact]
    public async Task Every_role_gets_exactly_the_documented_access()
    {
        var s = await Setup();
        await using (var db = Open(s.Target))
        {
            Assert.True(await new AccountService(db).RegisterAsync("f03admin", "f03admin@d01.example.invalid", Password));
            await db.Set<User>().Where(x => x.Email == "F03ADMIN@D01.EXAMPLE.INVALID").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Admin"));
        }
        // A request on the buyer's order line, so foreign users have a real object to probe.
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-2,SYSUTCDATETIME()) WHERE id={s.Order}");
        int dispute;
        await using (var db = Open(s.Target))
            dispute = (await new DisputeService(db, TimeProvider.System, new DisputeTiming()).OpenAsync(s.Buyer,
                new(s.Item, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, null))).DisputeId!.Value;

        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var otherBuyer = await Login(factory, "otherbuyer");
        using var seller = await Login(factory, "seller");
        using var otherSeller = await Login(factory, "otherseller");
        using var admin = await Login(factory, "f03admin");

        // Columns: anonymous, owning buyer, other buyer, owning seller, other seller, admin.
        var matrix = new (string Url, string[] Expected)[]
        {
            ("/Orders/Purchases", [Login, Ok, Ok, Forbidden, Forbidden, Forbidden]),
            ("/Orders/Sales", [Login, Forbidden, Forbidden, Ok, Ok, Forbidden]),
            ($"/Orders/Details/{s.Order}", [Login, Ok, NotFound, Ok, NotFound, Forbidden]),
            ("/Messages", [Login, Ok, Ok, Ok, Ok, Forbidden]),
            ("/api/v1/chat/conversations", [Unauthorized, Ok, Ok, Ok, Ok, Forbidden]),
            ("/Disputes", [Login, Ok, Ok, Ok, Ok, Forbidden]),
            ($"/Disputes/Details/{dispute}", [Login, Ok, NotFound, Ok, NotFound, Forbidden]),
            ($"/Disputes/NotReceived?orderItemId={s.Item}", [Login, "409", NotFound, Forbidden, Forbidden, Forbidden]),
            ("/Admin/Moderation", [Login, Forbidden, Forbidden, Forbidden, Forbidden, Ok]),
            ("/Admin/Disputes", [Login, Forbidden, Forbidden, Forbidden, Forbidden, Ok]),
            ($"/Admin/Dispute/{dispute}", [Login, Forbidden, Forbidden, Forbidden, Forbidden, Ok]),
            ($"/Checkout?productId={s.Product}", [Login, Ok, Ok, Forbidden, Forbidden, Forbidden]),
            // D02: an ineligible line (not delivered yet) renders the reason with 200 and no form.
            ($"/Reviews/Create?orderItemId={s.Item}", [Login, Ok, NotFound, Forbidden, Forbidden, Forbidden]),
            ("/Account/Me", [Login, Ok, Ok, Ok, Ok, Ok]),
            ($"/Catalog/Details/{s.Product}", [Ok, Ok, Ok, Ok, Ok, Ok]),
            ($"/Sellers/Feedback/{s.Seller}", [Ok, Ok, Ok, Ok, Ok, Ok]),
            ("/Help/Unavailable/save-seller", [Ok, Ok, Ok, Ok, Ok, Ok]),
        };
        var clients = new[] { ("anonymous", anonymous), ("buyer", buyer), ("other buyer", otherBuyer), ("seller", seller), ("other seller", otherSeller), ("admin", admin) };
        var mismatches = new List<string>();
        foreach (var (url, expected) in matrix)
            for (var i = 0; i < clients.Length; i++)
            {
                var actual = await Outcome(clients[i].Item2, url);
                if (actual != expected[i]) mismatches.Add($"{clients[i].Item1} {url}: expected {expected[i]}, got {actual}");
            }
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        // Browser GETs that end in 403 render a page instead of an empty body (F03).
        var denied = await buyer.GetAsync("/Admin/Moderation");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedBody = System.Net.WebUtility.HtmlDecode(await denied.Content.ReadAsStringAsync());
        Assert.True(deniedBody.Contains("You don't have access to this page."), "403 body: " + deniedBody[..Math.Min(300, deniedBody.Length)]);
        var reviewPage = await buyer.GetStringAsync($"/Reviews/Create?orderItemId={s.Item}");
        Assert.DoesNotContain("name=\"Rating\"", reviewPage); // Reason only, no form, until the order is delivered.
    }
}
