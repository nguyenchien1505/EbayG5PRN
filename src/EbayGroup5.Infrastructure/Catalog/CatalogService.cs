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
            Seller = seller.Username ?? "Seller", SellerId = seller.Id, CategoryId = product.CategoryId, Description = product.Description };

    public Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, CancellationToken cancellationToken = default) =>
        SearchAsync(query, categoryId, page, null, cancellationToken);

    public async Task<CatalogPage> SearchAsync(string? query, int? categoryId, int page, int? sellerId, CancellationToken cancellationToken = default)
    {
        query = (query ?? "").Trim();
        if (query.Length > 100 || categoryId is <= 0 || sellerId is <= 0 || page <= 0 || page > int.MaxValue / CatalogPage.PageSize)
            throw new ArgumentException("Invalid catalog filters.");
        var listings = Listings();
        if (query.Length > 0) listings = listings.Where(x => x.Title.Contains(query));
        if (categoryId != null) listings = listings.Where(x => x.CategoryId == categoryId);
        string? sellerName = null;
        if (sellerId != null)
        {
            listings = listings.Where(x => x.SellerId == sellerId);
            // Store name from the source Store table when the seller has one, otherwise the username.
            sellerName = await db.Set<User>().AsNoTracking().Where(x => x.Id == sellerId && x.Role == "Seller")
                .Select(x => db.Set<Store>().Where(s => s.SellerId == x.Id && s.StoreName != null && s.StoreName.Trim() != "").OrderBy(s => s.Id).Select(s => s.StoreName).FirstOrDefault()
                    ?? x.Username ?? "Seller").FirstOrDefaultAsync(cancellationToken);
        }
        var total = await listings.CountAsync(cancellationToken);
        var items = await listings.OrderBy(x => x.Id).Skip((page - 1) * CatalogPage.PageSize)
            .Take(CatalogPage.PageSize).ToListAsync(cancellationToken);
        var categories = await db.Set<Category>().AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CatalogCategory(x.Id, x.Name ?? "Uncategorized")).ToListAsync(cancellationToken);
        return new CatalogPage(items, categories, total, page, query, categoryId) { SellerId = sellerId, SellerName = sellerName };
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
