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
public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, IReadOnlyList<CatalogCategory> Categories, int Total, int Page, string Query, int? CategoryId)
{
    /// <summary>H02: "Visit store" / "Seller's other items" filter; SellerName is set when the id is an existing Seller.</summary>
    public int? SellerId { get; init; }
    public string? SellerName { get; init; }
    public const int PageSize = 12;
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
public sealed record CatalogDetail(CatalogItem Item, long Quantity);

public interface ICatalogService
{
    Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, CancellationToken cancellationToken = default);
    Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, int? sellerId, CancellationToken cancellationToken = default);
    Task<CatalogDetail?> FindAsync(int id, CancellationToken cancellationToken = default);
}
