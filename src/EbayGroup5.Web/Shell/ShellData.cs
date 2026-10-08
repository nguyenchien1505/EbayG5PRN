using System.Security.Claims;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EbayGroup5.Web.Shell;

/// <summary>
/// H03 header data (references HD/HM): categories for "Shop by category", the search scope and the category bar (cached 60 s),
/// and the "Ship to" place from the signed-in member's default address. Failures never break the page (empty values).
/// </summary>
public sealed class ShellData(ICatalogService catalog, EbayDbContext db, IMemoryCache cache, ILogger<ShellData> logger)
{
    private const string CategoriesKey = "shell:categories";

    public async Task<IReadOnlyList<CatalogCategory>> CategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CategoriesKey, out IReadOnlyList<CatalogCategory>? cached) && cached != null) return cached;
        try
        {
            var categories = await catalog.CategoriesAsync(cancellationToken);
            cache.Set(CategoriesKey, categories, TimeSpan.FromSeconds(60));
            return categories;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Header categories unavailable");
            return [];
        }
    }

    /// <summary>City (or country) of the member's default address, like eBay's "Ship to"; null when unknown.</summary>
    public async Task<string?> ShipToAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        try
        {
            var address = await db.Set<Address>().AsNoTracking().Where(x => x.UserId == id)
                .OrderByDescending(x => x.IsDefault == true).ThenByDescending(x => x.Id)
                .Select(x => new { x.City, x.Country }).FirstOrDefaultAsync(cancellationToken);
            var place = string.IsNullOrWhiteSpace(address?.City) ? address?.Country : address.City;
            return string.IsNullOrWhiteSpace(place) ? null : place.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ship-to place unavailable");
            return null;
        }
    }
}
