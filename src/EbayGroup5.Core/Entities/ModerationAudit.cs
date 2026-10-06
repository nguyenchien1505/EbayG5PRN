namespace EbayGroup5.Core.Entities;

public sealed class ModerationAudit
{
    public int Id { get; set; }
    public int? ReviewId { get; set; }
    public int? SellerFeedbackId { get; set; }
    public int AdminId { get; set; }
    public string Action { get; set; } = "";
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
