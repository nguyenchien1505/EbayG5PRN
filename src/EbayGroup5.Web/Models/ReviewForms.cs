using System.ComponentModel.DataAnnotations;
using EbayGroup5.Application.Catalog;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Reviews;

namespace EbayGroup5.Web.Models;

// Only these fields are bound; reviewer, seller, product and timestamps always come from the server.
public sealed class ReviewForm
{
    [Range(1, int.MaxValue)] public int OrderItemId { get; set; }
    public FeedbackSentiment? Sentiment { get; set; }
    public string? SellerComment { get; set; }
    [Range(1, 5)] public int? Rating { get; set; }
    public string? Comment { get; set; }
}

public sealed record ReviewPage(ReviewItemContext Context, FeedbackLineEligibility Feedback, ReviewForm Form);
public sealed record ProductReviewsView(CatalogItem Item, ProductReviewPage Reviews);
