namespace EbayGroup5.Core.Entities;

public sealed class Message
{
    public int Id { get; set; }
    public int? SenderId { get; set; }
    public int? ReceiverId { get; set; }
    public string? Content { get; set; }
    public DateTime? Timestamp { get; set; }
    public int? ConversationId { get; set; }
    public int? ConversationBuyerId { get; set; }
    public int? ConversationSellerId { get; set; }
    public long? Sequence { get; set; }
    public Guid? ClientMessageId { get; set; }
    public byte[]? PayloadHash { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? TraceId { get; set; }
    public int? ContextProductId { get; set; }
    public int? ContextOrderItemId { get; set; }
    public string? ContextTitle { get; set; }
    public decimal? ContextUnitPrice { get; set; }
}
