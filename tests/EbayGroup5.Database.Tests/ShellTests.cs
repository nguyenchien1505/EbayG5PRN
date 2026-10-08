using System.Net;
using EbayGroup5.Infrastructure.Catalog;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

// H03 (SHELL.md, Quyết định H03): eBay header, category bar, footer and home page from real data; notices for missing features.
public sealed class ShellTests
{
    [Fact]
    public async Task Home_and_header_render_ebay_controls_with_real_categories_and_listings()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        var categories = await new CatalogService(Open(s.Target)).CategoriesAsync();
        Assert.NotEmpty(categories);

        var home = await anonymous.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode); // No longer a redirect to the catalog.
        var html = WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync());
        foreach (var text in new[] { "Hi! ", "Sign in", "register", "Deals", "Brand Outlet", "Help & Contact", "Sell", "Watchlist", "My eBay",
                     "aria-label=\"Notifications\"", "aria-label=\"Shopping cart\"", "Shop by", "All Categories", "Advanced", "Saved",
                     "Shop with confidence", "Shop by category", "Shopping made easy", "Get more with an eBay account", "Recently listed",
                     "Wireless Headphones", "Money Back Guarantee", "Not affiliated with eBay Inc." })
            Assert.Contains(text, html);
        Assert.Contains($"/Catalog?categoryId={categories[0].Id}", html);
        Assert.Contains($"<option value=\"{categories[0].Id}\">{categories[0].Name}</option>", html);
        Assert.DoesNotContain("Ship to", html);

        var signedIn = WebUtility.HtmlDecode(await buyer.GetStringAsync("/"));
        Assert.Contains("Hi <strong>buyer</strong>!", signedIn);
        Assert.Contains("Example city", signedIn);               // Ship to: default address city.
        Assert.Contains("Purchase history", signedIn);
        Assert.Contains("action=\"/Account/Logout\"", signedIn);
        Assert.DoesNotContain("Get more with an eBay account", signedIn);

        // The category scope in the header search filters like the catalog filter.
        var scoped = await anonymous.GetAsync($"/Catalog?q=&categoryId={categories[0].Id}");
        Assert.Equal(HttpStatusCode.OK, scoped.StatusCode);
        Assert.Contains($"<option value=\"{categories[0].Id}\" selected=\"selected\">", await scoped.Content.ReadAsStringAsync());

        // Sign-in and register keep the compact header without search.
        Assert.DoesNotContain("id=\"site-query\"", await anonymous.GetStringAsync("/Account/Login"));
    }

    [Fact]
    public async Task Every_header_and_footer_notice_link_renders_the_demo_notice()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var home = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/"));
        var keys = System.Text.RegularExpressions.Regex.Matches(home, "/Help/Unavailable/([a-z-]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(17, keys.Count); // Every shell key except save-seller (item page).
        foreach (var key in keys.Append("save-seller"))
        {
            var page = await anonymous.GetAsync($"/Help/Unavailable/{key}");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("This feature isn't available in this demo", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/Help/Unavailable/DEALS")).StatusCode); // Keys are exact.
    }

    [Fact]
    public async Task Recent_listings_are_newest_fixed_price_items_and_take_is_bounded()
    {
        var s = await Setup();
        var catalog = new CatalogService(Open(s.Target));
        SqlFixture.Execute(s.Target, $"INSERT dbo.Product(title,price,sellerId,isAuction) VALUES(N'Brand new listing',3,{s.Seller},0),(N'An auction',3,{s.Seller},1)");
        var recent = await catalog.RecentAsync(3);
        Assert.Equal("Brand new listing", recent[0].Title);
        Assert.DoesNotContain(recent, x => x.Title == "An auction");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => catalog.RecentAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => catalog.RecentAsync(25));
    }
}
