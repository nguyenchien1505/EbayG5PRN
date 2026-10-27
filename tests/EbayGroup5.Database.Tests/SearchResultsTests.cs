using System.Net;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Infrastructure.Catalog;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

// H04 (CATALOG.md, Quyết định H04): eBay search results — sort, price range, buying format, category counts, sold counts, page sizes.
public sealed class SearchResultsTests
{
    [Fact]
    public async Task Search_sorts_filters_counts_and_reports_sold_quantities()
    {
        var s = await Setup();
        var category = int.Parse(SqlFixture.Scalar(s.Target, "INSERT dbo.Category(name) VALUES(N'Gadgets'); SELECT SCOPE_IDENTITY();"));
        int Product(string title, decimal price, int? cat = null) => int.Parse(SqlFixture.Scalar(s.Target,
            $"INSERT dbo.Product(title,price,categoryId,sellerId,isAuction) VALUES(N'{title}',{price.ToString(System.Globalization.CultureInfo.InvariantCulture)},{(cat?.ToString() ?? "NULL")},{s.Seller},0); SELECT SCOPE_IDENTITY();"));
        var cheap = Product("Gadget cheap", 5m, category);
        var mid = Product("Gadget mid", 20m, category);
        var dear = Product("Big gadget", 90m);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Product(title,price,categoryId,sellerId,isAuction) VALUES(N'Gadget auction',1,{category},{s.Seller},1)");
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderTable(buyerId,addressId,orderDate,totalPrice,status,SellerId) VALUES({s.Buyer},{s.Address},SYSUTCDATETIME(),10,'Pending',{s.Seller}); INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES(SCOPE_IDENTITY(),{mid},4,20)");
        var catalog = new CatalogService(Open(s.Target));
        async Task<int[]> Ids(CatalogQuery q) => (await catalog.SearchAsync(q)).Items.Select(x => x.Id).ToArray();

        Assert.Equal(new[] { cheap, mid, dear }, await Ids(new("gadget", Sort: CatalogSort.BestMatch))); // "Gadget…" titles first.
        Assert.Equal(new[] { cheap, mid, dear }, await Ids(new("gadget", Sort: CatalogSort.PriceLowest)));
        Assert.Equal(new[] { dear, mid, cheap }, await Ids(new("gadget", Sort: CatalogSort.PriceHighest)));
        Assert.Equal(new[] { dear, mid, cheap }, await Ids(new("gadget", Sort: CatalogSort.Newest)));
        Assert.Equal(new[] { mid }, await Ids(new("gadget", MinPrice: 10, MaxPrice: 20)));
        Assert.Empty(await Ids(new("gadget", Format: CatalogFormat.Auction))); // Fixed-price listings only.
        Assert.Equal(3, (await catalog.SearchAsync(new CatalogQuery("gadget", Format: CatalogFormat.BuyItNow))).Total);

        var page = await catalog.SearchAsync(new CatalogQuery("gadget", category));
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.CategoryCounts[category]); // Counts ignore the category filter itself.
        Assert.Equal(4, page.Sold[mid]);
        Assert.False(page.Sold.ContainsKey(cheap));
        Assert.Equal(1, (await catalog.SearchAsync(new CatalogQuery("gadget", PageSize: 12, Page: 1))).TotalPages);

        foreach (var bad in new CatalogQuery[] { new(PageSize: 10), new(MinPrice: -1), new(MinPrice: 5, MaxPrice: 4), new(MaxPrice: 100_000_001m),
                     new(Sort: (CatalogSort)9), new(Format: (CatalogFormat)9), new(Page: 0) })
            await Assert.ThrowsAsync<ArgumentException>(() => catalog.SearchAsync(bad));
    }

    [Fact]
    public async Task Http_results_page_renders_ebay_controls_and_rejects_bad_parameters()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var html = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?q=Headphones"));
        foreach (var text in new[] { "result", "for <strong>Headphones</strong>", "Category", "Price", "Buying Format", "All Listings", "Auction", "Buy It Now",
                     "Sort: Best Match", "Price + Shipping: lowest first", "Time: newly listed", "List View", "Gallery View", "Save this search",
                     "Free delivery", "Items Per Page", "Page 1 of 1", "Wireless Headphones" })
            Assert.Contains(text, html);
        Assert.Contains("srp-list", html);
        Assert.Contains("srp-gallery", await anonymous.GetStringAsync("/Catalog?q=Headphones&view=gallery"));
        Assert.Contains("No exact matches found", WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?format=auction")));
        Assert.Contains("Sort: Price + Shipping: highest first", WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Catalog?sort=price_desc&ipp=24&minPrice=1&maxPrice=100")));
        foreach (var query in new[] { "sort=cheap", "format=used", "ipp=10", "view=grid", "minPrice=-1", "minPrice=9&maxPrice=1", "maxPrice=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/Catalog?" + query)).StatusCode);
    }

    [Fact]
    public void Product_images_keep_only_local_product_paths()
    {
        Assert.Equal(new[] { "/img/products/a-1.svg", "/img/products/b.webp" },
            CatalogImages.Parse("""["/img/products/a-1.svg","https://evil.example/x.png","/img/products/../x.svg",1,"<img src=x>","/img/products/b.webp"]"""));
        foreach (var raw in new[] { null, "", "not json", "{\"a\":1}", "\"/img/products/a.svg\"" }) Assert.Empty(CatalogImages.Parse(raw));
        Assert.Equal(CatalogImages.MaxImages, CatalogImages.Parse("[" + string.Join(',', Enumerable.Repeat("\"/img/products/a.svg\"", 20)) + "]").Count);
    }

    // H05 (CATALOG.md, Quyết định H05): eBay item page with gallery, quantity, cart/watchlist notices and shipping/returns/payments.
    [Fact]
    public async Task Http_item_page_renders_ebay_layout_and_seeded_images()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var html = WebUtility.HtmlDecode(await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}"));
        foreach (var text in new[] { "Listed in category: <a href=\"/Catalog?categoryId=", "/img/products/headphones-1.svg", "data-gallery-src=\"/img/products/headphones-2.svg\"",
                     "Quantity:", "max=\"19\"", "19 available", $"/Checkout?productId={s.Product}", "/Help/Unavailable/cart\">Add to cart", "Add to Watchlist",
                     "Returns accepted.", "Shipping, returns, and payments", "Free</strong> Standard Shipping", "Estimated on", "30 days returns", "Cash on delivery",
                     "eBay Money Back Guarantee", "About this item", $"eBay item number: <strong>{s.Product}</strong>", "Item specifics", "Item description from the seller",
                     "Ratings and reviews", "About this seller" })
            Assert.Contains(text, html);
        // No condition/location claims: the schema has no such data.
        Assert.DoesNotContain("Condition:", html);
        // A listing without valid images falls back to the placeholder; legacy URLs are never rendered.
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Product SET images=N'[\"https://evil.example/a.png\"]' WHERE id={s.OtherProduct}");
        var other = await anonymous.GetStringAsync($"/Catalog/Details/{s.OtherProduct}");
        Assert.Contains("Image unavailable", other);
        Assert.DoesNotContain("evil.example", other);
        Assert.Contains("/img/products/headphones-1.svg", await anonymous.GetStringAsync("/Catalog?q=Headphones"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/img/products/headphones-1.svg")).StatusCode);
    }
}
