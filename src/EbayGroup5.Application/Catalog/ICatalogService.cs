namespace EbayGroup5.Application.Catalog;

public sealed record CatalogItem
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public decimal Price { get; init; }
    public required string Seller { get; init; }
    public int SellerId { get; init; }
    public int? CategoryId { get; init; }
    public string? Description { get; init; }
}
public sealed record CatalogCategory(int Id, string Name);
/// <summary>H04 eBay sort options: Best Match, Price lowest/highest first, Time: newly listed.</summary>
public enum CatalogSort { BestMatch, PriceLowest, PriceHighest, Newest }
/// <summary>H04 buying format tabs. The clone only lists fixed-price items, so Auction is always empty.</summary>
public enum CatalogFormat { All, Auction, BuyItNow }

/// <summary>H04 search request (SR). Prices are inclusive; PageSize is one of PageSizes.</summary>
public sealed record CatalogQuery(string? Query = null, int? CategoryId = null, int? SellerId = null, decimal? MinPrice = null, decimal? MaxPrice = null,
    CatalogSort Sort = CatalogSort.BestMatch, CatalogFormat Format = CatalogFormat.All, int Page = 1, int PageSize = CatalogPage.PageSize)
{
    public static readonly int[] PageSizes = [12, 24, 48];
    public const decimal MaxPriceFilter = 100_000_000m;
}

public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, IReadOnlyList<CatalogCategory> Categories, int Total, int Page, string Query, int? CategoryId)
{
    /// <summary>H02: "Visit store" / "Seller's other items" filter; SellerName is set when the id is an existing Seller.</summary>
    public int? SellerId { get; init; }
    public string? SellerName { get; init; }
    public const int PageSize = 12;
    public CatalogQuery Request { get; init; } = new();
    /// <summary>Result count per category for the same search without the category filter (eBay category rail).</summary>
    public IReadOnlyDictionary<int, int> CategoryCounts { get; init; } = new Dictionary<int, int>();
    /// <summary>Quantity sold per listing on this page (orders snapshot), only listings that sold something.</summary>
    public IReadOnlyDictionary<int, long> Sold { get; init; } = new Dictionary<int, long>();
    public int TotalPages => (int)Math.Ceiling(Total / (double)Request.PageSize);
}
public sealed record CatalogDetail(CatalogItem Item, long Quantity);

public interface ICatalogService
{
    Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, CancellationToken cancellationToken = default);
    Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, int? sellerId, CancellationToken cancellationToken = default);
    /// <summary>H04 full search; invalid values throw ArgumentException.</summary>
    Task<CatalogPage> SearchAsync(CatalogQuery request, CancellationToken cancellationToken = default);
    Task<CatalogDetail?> FindAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>H03 header "Shop by category", category bar and search scope; ordered by name.</summary>
    Task<IReadOnlyList<CatalogCategory>> CategoriesAsync(CancellationToken cancellationToken = default);
    /// <summary>H03 home page strip: newest fixed-price listings (same listing rule as search), at most 24.</summary>
    Task<IReadOnlyList<CatalogItem>> RecentAsync(int take, CancellationToken cancellationToken = default);
}
