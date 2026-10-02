namespace EbayGroup5.Core.Entities;

public sealed class Bid
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? BidderId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? BidTime { get; set; }
}
