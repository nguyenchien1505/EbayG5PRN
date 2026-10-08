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
    [Range(1, 5)] public int? Rating { get; set; }
    public string? Comment { get; set; }
    public bool? ArrivedOnTime { get; set; }
    [Range(1, 5)] public int? DescriptionRating { get; set; }
    [Range(1, 5)] public int? ShippingTimeRating { get; set; }
    [Range(1, 5)] public int? CommunicationRating { get; set; }

    public FeedbackDetails Details => new(ArrivedOnTime, DescriptionRating, ShippingTimeRating, CommunicationRating);
}

public sealed record ReviewPage(ReviewItemContext Context, FeedbackLineEligibility Feedback, ReviewForm Form);
public sealed record ProductReviewsView(CatalogItem Item, ProductReviewPage Reviews);
