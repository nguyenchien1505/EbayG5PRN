using System.Security.Claims;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Checkout;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EbayGroup5.Web.Controllers;

[Authorize(Policy = "Buyer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(ICatalogService catalog, ICheckoutService checkout, IFeedbackService feedback, DisputeTiming timing) : Controller
{
    private int BuyerId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Index(int productId, int quantity = 1, int? addressId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || productId <= 0 || quantity is < 1 or > 99) return BadRequest();
        var product = await catalog.FindAsync(productId, cancellationToken);
        if (product == null) return NotFound();
        var addresses = await checkout.AddressesAsync(BuyerId, cancellationToken);
        return View(await PageAsync(new CheckoutForm { ProductId = productId, Quantity = quantity,
            AddressId = addresses.FirstOrDefault(x => x.Id == addressId)?.Id ?? addresses.FirstOrDefault()?.Id ?? 0,
            ExpectedUnitPrice = product.Item.Price, Key = Guid.NewGuid() }, product, addresses, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Place(CheckoutForm form, CancellationToken cancellationToken)
    {
        CheckoutResult? result = null;
        if (ModelState.IsValid) result = await checkout.PlaceAsync(BuyerId,
            new(form.ProductId, form.AddressId, form.Quantity, form.ExpectedUnitPrice, form.Key), cancellationToken);
        if (result?.OrderId is int orderId) return RedirectToAction(nameof(Confirmation), new { id = orderId });
        ModelState.AddModelError("", result?.Error ?? "Check your checkout details.");
        Response.StatusCode = result?.Conflict == true ? 409 : 400;
        var product = await catalog.FindAsync(form.ProductId, cancellationToken);
        if (product == null) return NotFound();
        // Show the refreshed quote. A conflict never commits the request key.
        form.ExpectedUnitPrice = product.Item.Price;
        ModelState.Remove(nameof(form.ExpectedUnitPrice));
        return View("Index", await PageAsync(form, product, await checkout.AddressesAsync(BuyerId, cancellationToken), cancellationToken));
    }

    // H06a (CHECKOUT.md, C2): seller line and the same estimated delivery the item page shows and the order stores.
    private async Task<CheckoutPage> PageAsync(CheckoutForm form, CatalogDetail product, IReadOnlyList<EbayGroup5.Core.Entities.Address> addresses, CancellationToken cancellationToken) =>
        new(form, product, addresses, await feedback.ReputationAsync(product.Item.SellerId, cancellationToken), timing.EstimatedDelivery(DateTime.UtcNow));

    [HttpGet]
    public async Task<IActionResult> AddAddress(int productId, int quantity = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || quantity is < 1 or > 99) return BadRequest();
        if (await catalog.FindAsync(productId, cancellationToken) == null) return NotFound();
        return View(new AddressForm { ProductId = productId, Quantity = quantity });
    }

    [HttpPost]
    public async Task<IActionResult> AddAddress(AddressForm form, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            if (await catalog.FindAsync(form.ProductId, cancellationToken) == null) return NotFound();
            var id = await checkout.AddAddressAsync(BuyerId, new(form.FullName, form.Phone, form.Street, form.City, form.State, form.Country), cancellationToken);
            if (id != null) return RedirectToAction(nameof(Index), new { productId = form.ProductId, quantity = form.Quantity, addressId = id });
            ModelState.AddModelError("", "Check your shipping address.");
        }
        Response.StatusCode = 400;
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Confirmation(int id, CancellationToken cancellationToken)
    {
        var order = await checkout.FindAsync(BuyerId, id, cancellationToken);
        return order == null ? NotFound() : View(order);
    }
}
