namespace EbayGroup5.Core.Entities;

public sealed class ChatConversation
{
    public int Id { get; set; }
    public int BuyerId { get; set; }
    public int SellerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long LastSequence { get; set; }
    public long BuyerReadSequence { get; set; }
    public long SellerReadSequence { get; set; }
    public int? ContextProductId { get; set; }
    public int? ContextOrderItemId { get; set; }
    public string? ContextTitle { get; set; }
    public decimal? ContextUnitPrice { get; set; }
}
