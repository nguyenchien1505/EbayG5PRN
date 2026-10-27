using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

// ADR-003 §2: eBay controls outside the clone's scope open one honest notice instead of a dead link. Fixed keys only.
public sealed class HelpController : Controller
{
    internal static readonly IReadOnlyDictionary<string, string> Features = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["save-seller"] = "Save seller",
        // H03 header, category bar and footer.
        ["deals"] = "Deals", ["brand-outlet"] = "Brand Outlet", ["help-contact"] = "Help & Contact", ["sell"] = "Selling on eBay",
        ["watchlist"] = "Watchlist", ["notifications"] = "Notifications", ["cart"] = "Shopping cart", ["image-search"] = "Search by image",
        ["advanced-search"] = "Advanced search", ["saved"] = "Saved searches and sellers", ["buying-help"] = "Bidding & buying help",
        ["charity"] = "eBay for Charity", ["seller-center"] = "Seller Center", ["about"] = "About eBay", ["policies"] = "Policies",
        ["community"] = "Community", ["site-map"] = "Site map",
        // H06a register: only personal (Buyer) accounts exist.
        ["business-account"] = "Business accounts",
    };

    [HttpGet]
    public IActionResult Unavailable(string id)
    {
        if (!Features.TryGetValue(id ?? "", out var feature)) return NotFound();
        ViewData["Feature"] = feature;
        return View();
    }
}
