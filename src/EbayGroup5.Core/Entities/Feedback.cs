namespace EbayGroup5.Core.Entities;

public sealed class Feedback
{
    public int Id { get; set; }
    public int? SellerId { get; set; }
    public decimal? AverageRating { get; set; }
    public int? TotalReviews { get; set; }
    public decimal? PositiveRate { get; set; }
}
