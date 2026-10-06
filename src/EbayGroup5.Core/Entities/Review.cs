namespace EbayGroup5.Core.Entities;

public sealed class Review
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? ReviewerId { get; set; }
    public int? OrderItemId { get; set; }
    public int? Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? HiddenAtUtc { get; set; }
    public DateTime? CommentRemovedAtUtc { get; set; }
}
