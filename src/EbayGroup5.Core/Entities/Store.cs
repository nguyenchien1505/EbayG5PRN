namespace EbayGroup5.Core.Entities;

public sealed class Store
{
    public int Id { get; set; }
    public int? SellerId { get; set; }
    public string? StoreName { get; set; }
    public string? Description { get; set; }
    public string? BannerImageURL { get; set; }
}
