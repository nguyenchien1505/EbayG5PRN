namespace EbayGroup5.Core.Entities;

/// <summary>Server-side request draft (E02, DbUp 0012); one per buyer, order line and kind.</summary>
public sealed class DisputeDraft
{
    public int Id { get; set; }
    public int BuyerId { get; set; }
    public int OrderItemId { get; set; }
    public string Kind { get; set; } = "";
    public string? Reason { get; set; }
    public string? Preference { get; set; }
    public string? Details { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
