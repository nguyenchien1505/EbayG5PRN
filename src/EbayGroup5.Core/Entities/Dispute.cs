namespace EbayGroup5.Core.Entities;

public sealed class Dispute
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? RaisedBy { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Resolution { get; set; }
    // E01 (DbUp 0011): module rows have OrderItemId set; legacy rows keep the baseline columns only.
    public int? OrderItemId { get; set; }
    public int? SellerId { get; set; }
    public string? Kind { get; set; }
    public string? Reason { get; set; }
    public string? Preference { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? SellerRespondedAtUtc { get; set; }
    public DateTime? EscalatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? Outcome { get; set; }
    public decimal? OfferAmount { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? ReturnTracking { get; set; }
}
