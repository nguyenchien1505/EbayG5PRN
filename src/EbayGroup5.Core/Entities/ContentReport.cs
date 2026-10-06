namespace EbayGroup5.Core.Entities;

public sealed class ContentReport
{
    public int Id { get; set; }
    public int? ReviewId { get; set; }
    public int? SellerFeedbackId { get; set; }
    public int ReporterId { get; set; }
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedById { get; set; }
}
