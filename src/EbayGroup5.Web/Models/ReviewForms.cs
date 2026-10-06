using System.ComponentModel.DataAnnotations;

namespace EbayGroup5.Web.Models;

// Only these fields are bound; reviewer, product and timestamps always come from the server.
public sealed class ReviewForm
{
    [Range(1, int.MaxValue)] public int OrderItemId { get; set; }
    [Range(1, 5)] public int Rating { get; set; }
    public string? Comment { get; set; }
}
