namespace EbayGroup5.Core.Entities;

public sealed class SellerFeedback
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public int BuyerId { get; set; }
    public int SellerId { get; set; }
    public string Sentiment { get; set; } = "";
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
