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
    public string? ReplyText { get; set; }
    public DateTime? RepliedAtUtc { get; set; }
    public DateTime? HiddenAtUtc { get; set; }
    public DateTime? CommentRemovedAtUtc { get; set; }
    public bool? ArrivedOnTime { get; set; }
    public byte? DescriptionRating { get; set; }
    public byte? ShippingTimeRating { get; set; }
    public byte? CommunicationRating { get; set; }
    public DateOnly? TransactionWeek { get; set; }
}
