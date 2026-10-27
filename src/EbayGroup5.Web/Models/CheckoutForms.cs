using System.ComponentModel.DataAnnotations;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Core.Entities;

namespace EbayGroup5.Web.Models;

public sealed class CheckoutForm
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, int.MaxValue)] public int AddressId { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
    public decimal ExpectedUnitPrice { get; set; }
    public Guid Key { get; set; }
}

public sealed record CheckoutPage(CheckoutForm Form, CatalogDetail Product, IReadOnlyList<Address> Addresses, SellerReputation Reputation, DateTime EstimatedDelivery);

public sealed class AddressForm
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
    [Required, StringLength(100), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, StringLength(20), Display(Name = "Phone number")] public string Phone { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Street address")] public string Street { get; set; } = "";
    [Required, StringLength(50)] public string City { get; set; } = "";
    [StringLength(50), Display(Name = "State / Region (optional)")] public string? State { get; set; }
    [Required, StringLength(50), Display(Name = "Country or region")] public string Country { get; set; } = "";
}
