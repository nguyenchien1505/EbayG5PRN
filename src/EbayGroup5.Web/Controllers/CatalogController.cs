using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

public sealed class CatalogController(ICatalogService catalog, IReviewService reviews, IFeedbackService feedback) : Controller
{
    [HttpGet]
    // H04 (reference SR): sort, buying format tab, price range, items per page and list/gallery view on top of q/category/seller.
    public async Task<IActionResult> Index(string? q, int? categoryId, int? seller, int page = 1, string? sort = null, string? format = null,
        decimal? minPrice = null, decimal? maxPrice = null, int ipp = CatalogPage.PageSize, string? view = null, CancellationToken cancellationToken = default)
    {
        CatalogSort? catalogSort = sort switch { null or "" or "best" => CatalogSort.BestMatch, "price_asc" => CatalogSort.PriceLowest, "price_desc" => CatalogSort.PriceHighest, "newest" => CatalogSort.Newest, _ => null };
        CatalogFormat? catalogFormat = format switch { null or "" or "all" => CatalogFormat.All, "auction" => CatalogFormat.Auction, "bin" => CatalogFormat.BuyItNow, _ => null };
        if (!ModelState.IsValid || (q?.Trim().Length ?? 0) > 100 || categoryId is <= 0 || seller is <= 0 || page <= 0 || catalogSort == null || catalogFormat == null
            || !CatalogQuery.PageSizes.Contains(ipp) || page > int.MaxValue / ipp || view is not (null or "" or "list" or "gallery")
            || minPrice is < 0 or > CatalogQuery.MaxPriceFilter || maxPrice is < 0 or > CatalogQuery.MaxPriceFilter || (minPrice > maxPrice && maxPrice != null))
        {
            Response.StatusCode = 400;
            return View("InvalidFilters");
        }
        var result = await catalog.SearchAsync(new CatalogQuery(q, categoryId, seller, minPrice, maxPrice, catalogSort.Value, catalogFormat.Value, page, ipp), cancellationToken);
        ViewData["Gallery"] = view == "gallery";
        ViewData["SearchQuery"] = result.Query;
        // D06 (A2): card rating and seller reputation, batched for the current page only.
        ViewData["Ratings"] = await reviews.SummariesAsync(result.Items.Select(x => x.Id).ToArray(), cancellationToken);
        ViewData["Reputations"] = await feedback.ReputationsAsync(result.Items.Select(x => x.SellerId).ToArray(), cancellationToken);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? fb = null, CancellationToken cancellationToken = default)
    {
        // H02: "Seller feedback" dropdown on the item page (All ratings / Positive / Neutral / Negative).
        FeedbackSentiment? filter = fb switch { "positive" => FeedbackSentiment.Positive, "neutral" => FeedbackSentiment.Neutral, "negative" => FeedbackSentiment.Negative, _ => null };
        if (!ModelState.IsValid || fb is not (null or "" or "positive" or "neutral" or "negative"))
        {
            Response.StatusCode = 400;
            return View("InvalidFilters");
        }
        var result = await catalog.FindAsync(id, cancellationToken);
        if (result == null) return NotFound();
        ViewData["Reviews"] = await reviews.ProductReviewsAsync(id, cancellationToken: cancellationToken);
        var sellerFeedback = await feedback.ProfileAsync(result.Item.SellerId, new FeedbackProfileQuery(Sentiment: filter, PageSize: 8), cancellationToken);
        ViewData["SellerReputation"] = sellerFeedback?.Reputation ?? await feedback.ReputationAsync(result.Item.SellerId, cancellationToken);
        if (sellerFeedback?.Summary is { } summary)
            ViewData["AboutSeller"] = new Models.AboutSellerView(id, summary, sellerFeedback.Reputation, feedback.DetailedRatingsMinimum, sellerFeedback, filter?.ToString().ToLowerInvariant());
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Reviews(int id, int page = 1, string? sort = null, CancellationToken cancellationToken = default)
    {
        ReviewSort? order = (sort ?? "recent") switch { "recent" => ReviewSort.Recent, "highest" => ReviewSort.Highest, "lowest" => ReviewSort.Lowest, _ => null };
        if (!ModelState.IsValid || order == null || page <= 0 || page > int.MaxValue / ProductReviewPage.PageSize)
        {
            Response.StatusCode = 400;
            return View("InvalidReviewFilters", id);
        }
        var result = await catalog.FindAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return View(new ProductReviewsView(result.Item, await reviews.ProductReviewsAsync(id, page, order.Value, cancellationToken)));
    }
}
