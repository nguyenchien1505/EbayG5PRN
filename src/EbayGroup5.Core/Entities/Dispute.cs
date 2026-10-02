namespace EbayGroup5.Core.Entities;

public sealed class Dispute
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? RaisedBy { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Resolution { get; set; }
}
