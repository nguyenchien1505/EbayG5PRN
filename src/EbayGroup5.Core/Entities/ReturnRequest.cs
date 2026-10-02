namespace EbayGroup5.Core.Entities;

public sealed class ReturnRequest
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? UserId { get; set; }
    public string? Reason { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
}
