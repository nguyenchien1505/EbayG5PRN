namespace EbayGroup5.Core.Entities;

public sealed class Product
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Images { get; set; }
    public int? CategoryId { get; set; }
    public int? SellerId { get; set; }
    public bool? IsAuction { get; set; }
    public DateTime? AuctionEndTime { get; set; }
}
