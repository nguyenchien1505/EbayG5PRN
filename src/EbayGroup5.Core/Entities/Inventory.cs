namespace EbayGroup5.Core.Entities;

public sealed class Inventory
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? Quantity { get; set; }
    public DateTime? LastUpdated { get; set; }
}
