namespace EbayGroup5.Core.Entities;

public sealed class OrderTable
{
    public int Id { get; set; }
    public int? BuyerId { get; set; }
    public int? AddressId { get; set; }
    public DateTime? OrderDate { get; set; }
    public decimal? TotalPrice { get; set; }
    public string? Status { get; set; }
    public Guid? CheckoutKey { get; set; }
    public string? CheckoutFingerprint { get; set; }
}
