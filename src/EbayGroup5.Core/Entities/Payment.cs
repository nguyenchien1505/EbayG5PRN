namespace EbayGroup5.Core.Entities;

public sealed class Payment
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? UserId { get; set; }
    public decimal? Amount { get; set; }
    public string? Method { get; set; }
    public string? Status { get; set; }
    public DateTime? PaidAt { get; set; }
}
