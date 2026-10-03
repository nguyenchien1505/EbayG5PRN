using EbayGroup5.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[ApiController, Route("api/v1/catalog")]
public sealed class CatalogApiController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(string? q, int? categoryId, int page = 1, CancellationToken cancellationToken = default)
    {
        if ((q?.Trim().Length ?? 0) > 100 || categoryId is <= 0 || page <= 0 || page > int.MaxValue / CatalogPage.PageSize)
            return Problem(statusCode: 400, title: "Invalid catalog filters.");
        return Ok(await catalog.SearchAsync(q, categoryId, page, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var item = await catalog.FindAsync(id, cancellationToken);
        return item == null ? Problem(statusCode: 404, title: "Item not found.") : Ok(item);
    }
}
