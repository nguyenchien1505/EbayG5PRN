namespace EbayGroup5.Core.Entities;

public sealed class ShippingInfo
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Status { get; set; }
    public DateTime? EstimatedArrival { get; set; }
}
