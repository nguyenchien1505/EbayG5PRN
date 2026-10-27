using EbayGroup5.Application.Catalog;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Catalog;

public sealed class CatalogService(EbayDbContext db) : ICatalogService
{
    private IQueryable<CatalogItem> Listings() =>
        from product in db.Set<Product>().AsNoTracking()
        join seller in db.Set<User>().AsNoTracking() on product.SellerId equals seller.Id
        where product.IsAuction == false && product.Title != null && product.Title.Trim() != "" &&
              product.Price != null && product.Price >= 0 && seller.Role == "Seller"
        select new CatalogItem { Id = product.Id, Title = product.Title!, Price = product.Price!.Value,
            Seller = seller.Username ?? "Seller", SellerId = seller.Id, CategoryId = product.CategoryId, Description = product.Description, ImageData = product.Images };

    public Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, CancellationToken cancellationToken = default) =>
        SearchAsync(new CatalogQuery(query, categoryId, Page: page), cancellationToken);

    public Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, int? sellerId, CancellationToken cancellationToken = default) =>
        SearchAsync(new CatalogQuery(query, categoryId, sellerId, Page: page), cancellationToken);

    public async Task<CatalogPage> SearchAsync(CatalogQuery request, CancellationToken cancellationToken = default)
    {
        var query = (request.Query ?? "").Trim();
        if (query.Length > 100 || request.CategoryId is <= 0 || request.SellerId is <= 0 || request.Page <= 0
            || !CatalogQuery.PageSizes.Contains(request.PageSize) || request.Page > int.MaxValue / request.PageSize
            || !Enum.IsDefined(request.Sort) || !Enum.IsDefined(request.Format)
            || request.MinPrice is < 0 or > CatalogQuery.MaxPriceFilter || request.MaxPrice is < 0 or > CatalogQuery.MaxPriceFilter
            || (request.MinPrice is { } low && request.MaxPrice is { } high && low > high))
            throw new ArgumentException("Invalid catalog filters.");
        var listings = Listings();
        // Fixed-price listings only (B04): the Auction tab is honestly empty.
        if (request.Format == CatalogFormat.Auction) listings = listings.Where(x => false);
        if (query.Length > 0) listings = listings.Where(x => x.Title.Contains(query));
        if (request.MinPrice is { } min) listings = listings.Where(x => x.Price >= min);
        if (request.MaxPrice is { } max) listings = listings.Where(x => x.Price <= max);
        string? sellerName = null;
        if (request.SellerId is { } sellerId)
        {
            listings = listings.Where(x => x.SellerId == sellerId);
            // Store name from the source Store table when the seller has one, otherwise the username.
            sellerName = await db.Set<User>().AsNoTracking().Where(x => x.Id == sellerId && x.Role == "Seller")
                .Select(x => db.Set<Store>().Where(s => s.SellerId == x.Id && s.StoreName != null && s.StoreName.Trim() != "").OrderBy(s => s.Id).Select(s => s.StoreName).FirstOrDefault()
                    ?? x.Username ?? "Seller").FirstOrDefaultAsync(cancellationToken);
        }
        var counts = await listings.Where(x => x.CategoryId != null).GroupBy(x => x.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        if (request.CategoryId != null) listings = listings.Where(x => x.CategoryId == request.CategoryId);
        var total = await listings.CountAsync(cancellationToken);
        var ordered = request.Sort switch
        {
            CatalogSort.PriceLowest => listings.OrderBy(x => x.Price).ThenBy(x => x.Id),
            CatalogSort.PriceHighest => listings.OrderByDescending(x => x.Price).ThenBy(x => x.Id),
            CatalogSort.Newest => listings.OrderByDescending(x => x.Id),
            // Best Match (clone approximation): titles starting with the search words first, then listing order.
            _ => query.Length > 0 ? listings.OrderByDescending(x => x.Title.StartsWith(query)).ThenBy(x => x.Id) : listings.OrderBy(x => x.Id)
        };
        var items = await ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        var ids = items.Select(x => x.Id).ToArray();
        var sold = await db.Set<OrderItem>().AsNoTracking()
            .Where(i => i.ProductId != null && ids.Contains(i.ProductId.Value) && i.Quantity > 0 && db.Set<OrderTable>().Any(o => o.Id == i.OrderId && o.SellerId != null))
            .GroupBy(i => i.ProductId!.Value).Select(g => new { g.Key, Sold = g.Sum(i => (long)i.Quantity!.Value) })
            .ToDictionaryAsync(x => x.Key, x => x.Sold, cancellationToken);
        var categories = await db.Set<Category>().AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CatalogCategory(x.Id, x.Name ?? "Uncategorized")).ToListAsync(cancellationToken);
        return new CatalogPage(items, categories, total, request.Page, query, request.CategoryId)
        {
            SellerId = request.SellerId, SellerName = sellerName, Request = request with { Query = query }, CategoryCounts = counts, Sold = sold
        };
    }

    public async Task<IReadOnlyList<CatalogCategory>> CategoriesAsync(CancellationToken cancellationToken = default) =>
        await db.Set<Category>().AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CatalogCategory(x.Id, x.Name ?? "Uncategorized")).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogItem>> RecentAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(take));
        return await Listings().OrderByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<CatalogDetail?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await Listings().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item == null) return null;
        var quantity = await db.Set<Inventory>().Where(x => x.ProductId == id && x.Quantity > 0)
            .SumAsync(x => (long?)x.Quantity, cancellationToken) ?? 0;
        return new CatalogDetail(item, quantity);
    }
}
