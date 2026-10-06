using System.Net;
using EbayGroup5.Application.Reviews;
using EbayGroup5.Infrastructure.Reviews;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

public sealed class ReviewDisplayTests
{
    // Inserts an order-linked review on a fresh line of the setup order; CHECK/FK constraints still apply.
    private static int AddReview(SetupData s, int rating, string? comment, int? product = null)
    {
        var text = comment == null ? "NULL" : "N'" + comment.Replace("'", "''") + "'";
        return int.Parse(SqlFixture.Scalar(s.Target, $"""
            INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({s.Order},{product ?? s.Product},1,49.90);
            INSERT dbo.Review(productId,reviewerId,OrderItemId,rating,comment,createdAt) VALUES({product ?? s.Product},{s.Buyer},SCOPE_IDENTITY(),{rating},{text},SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
            """));
    }

    [Fact]
    public async Task Summary_counts_only_order_linked_reviews_with_correct_average_rounding_and_distribution()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        var empty = await service.ProductReviewsAsync(s.Product);
        Assert.Equal(0, empty.Summary.Ratings);
        Assert.Null(empty.Summary.Average);
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, empty.Summary.Distribution);
        Assert.Empty(empty.Reviews);
        Assert.Equal(0, empty.TotalPages);

        AddReview(s, 5, "Excellent"); AddReview(s, 4, null); AddReview(s, 4, "Good"); AddReview(s, 4, null);
        AddReview(s, 1, "Other product", s.OtherProduct);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,rating,comment,createdAt) VALUES({s.Product},{s.Buyer},9,N'legacy high','20260901'),({s.Product},{s.Buyer},1,N'legacy low',NULL),({s.Product},NULL,NULL,NULL,NULL)");
        var page = await service.ProductReviewsAsync(s.Product);
        Assert.Equal(4, page.Summary.Ratings);
        Assert.Equal(2, page.Summary.Reviews);
        Assert.Equal(4.3m, page.Summary.Average); // 17 / 4 = 4.25 rounds away from zero.
        Assert.Equal(new[] { 1, 3, 0, 0, 0 }, page.Summary.Distribution);
        Assert.Equal(new[] { "Good", "Excellent" }, page.Reviews.Select(x => x.Comment).ToArray());
        Assert.All(page.Reviews, x => Assert.Equal("buyer", x.Reviewer));
        Assert.DoesNotContain(page.Reviews, x => x.Comment.StartsWith("legacy"));

        AddReview(s, 1, null);
        var lower = await service.ProductReviewsAsync(s.Product);
        Assert.Equal(3.6m, lower.Summary.Average); // 18 / 5
        Assert.Equal(new[] { 1, 3, 0, 0, 1 }, lower.Summary.Distribution);
        Assert.Equal(1m, (await service.ProductReviewsAsync(s.OtherProduct)).Summary.Average);
    }

    [Fact]
    public async Task Pages_are_bounded_stable_sorted_and_reject_invalid_filters()
    {
        var s = await Setup();
        var ids = new List<int>();
        for (var i = 0; i < 23; i++) ids.Add(AddReview(s, i % 5 + 1, "Review " + i));
        AddReview(s, 5, null); AddReview(s, 2, null);
        await using var db = Open(s.Target);
        var service = new ReviewService(db);
        var pages = new List<ProductReviewPage>();
        for (var p = 1; p <= 4; p++) pages.Add(await service.ProductReviewsAsync(s.Product, p));
        Assert.Equal(new[] { 10, 10, 3, 0 }, pages.Select(x => x.Reviews.Count).ToArray());
        Assert.All(pages, x => Assert.Equal(3, x.TotalPages));
        Assert.Equal(25, pages[0].Summary.Ratings);
        Assert.Equal(23, pages[0].Summary.Reviews);
        var seen = pages.SelectMany(x => x.Reviews.Select(r => r.Id)).ToList();
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(x => x), seen);

        var highest = new List<ProductReview>();
        var lowest = new List<ProductReview>();
        for (var p = 1; p <= 3; p++)
        {
            highest.AddRange((await service.ProductReviewsAsync(s.Product, p, ReviewSort.Highest)).Reviews);
            lowest.AddRange((await service.ProductReviewsAsync(s.Product, p, ReviewSort.Lowest)).Reviews);
        }
        Assert.Equal(highest.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Id).Select(x => x.Id), highest.Select(x => x.Id));
        Assert.Equal(lowest.OrderBy(x => x.Rating).ThenByDescending(x => x.Id).Select(x => x.Id), lowest.Select(x => x.Id));
        Assert.Equal(23, highest.Select(x => x.Id).Distinct().Count());
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProductReviewsAsync(s.Product, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProductReviewsAsync(s.Product, 1, (ReviewSort)9));
    }

    [Fact]
    public async Task Public_product_pages_show_encoded_verified_reviews_and_validate_filters()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var before = await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}");
        Assert.Contains("Ratings and reviews", before);
        Assert.Contains("No ratings yet", before);

        AddReview(s, 4, "<script>alert('x')</script> works\nsecond line");
        AddReview(s, 3, null);
        SqlFixture.Execute(s.Target, $"INSERT dbo.Review(productId,reviewerId,rating,comment) VALUES({s.Product},{s.Buyer},5,N'legacy hidden text')");
        var detail = await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}");
        Assert.Contains("3.5", detail);
        Assert.Contains("2 product ratings", detail);
        Assert.Contains("1 review", detail);
        Assert.Contains("Verified purchase", detail);
        Assert.Contains("&lt;script&gt;", detail);
        Assert.DoesNotContain("<script>alert", detail);
        Assert.DoesNotContain("legacy hidden text", detail);
        Assert.DoesNotContain("@d01.example.invalid", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"/Catalog/Reviews/{s.Product}", detail);

        var all = await anonymous.GetAsync($"/Catalog/Reviews/{s.Product}?sort=highest&page=1");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var html = await all.Content.ReadAsStringAsync();
        Assert.Contains("Ratings and reviews", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("No reviews on this page", await anonymous.GetStringAsync($"/Catalog/Reviews/{s.Product}?page=9"));
        foreach (var query in new[] { "?sort=newest", "?sort=Recent", "?page=0", "?page=-1", "?page=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync($"/Catalog/Reviews/{s.Product}{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/Catalog/Reviews/2147483647")).StatusCode);
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Product SET isAuction=1 WHERE id={s.Product}");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/Catalog/Reviews/{s.Product}")).StatusCode);
    }

    [Fact]
    public async Task Buyer_review_journey_renders_form_errors_notice_and_final_state()
    {
        var s = await Setup();
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var buyer = await Login(factory, "buyer");
        using var stranger = await Login(factory, "otherbuyer");
        using var seller = await Login(factory, "seller");
        var create = $"/Reviews/Create?orderItemId={s.Item}";

        var pending = await buyer.GetStringAsync($"/Orders/Details/{s.Order}");
        Assert.DoesNotContain("Leave a review", pending);
        Assert.Contains("after the order is delivered", pending);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(create)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(create)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync(create)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/Reviews/Create?orderItemId=abc")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync("/Reviews/Create?orderItemId=2147483647")).StatusCode);
        var early = await buyer.GetStringAsync(create);
        Assert.Contains("after the order is delivered", early);
        Assert.DoesNotContain("How would you rate this item?", early);

        await Deliver(s, s.Order);
        Assert.Contains($"/Reviews/Create?orderItemId={s.Item}", await buyer.GetStringAsync($"/Orders/Details/{s.Order}"));
        Assert.DoesNotContain("Leave a review", await seller.GetStringAsync($"/Orders/Details/{s.Order}"));
        var form = await buyer.GetStringAsync(create);
        Assert.Contains("How would you rate this item?", form);
        Assert.Contains("Tell us more about this item", form);
        Assert.Contains("0 / 2000", System.Text.RegularExpressions.Regex.Replace(form, "<[^>]+>", ""));

        var missing = await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["OrderItemId"] = s.Item.ToString(), ["Comment"] = "Keep this draft", ["__RequestVerificationToken"] = Field(form) }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingHtml = await missing.Content.ReadAsStringAsync();
        Assert.Contains("Select a star rating from 1 to 5.", missingHtml);
        Assert.Contains("Keep this draft", missingHtml);

        var tooLong = await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["OrderItemId"] = s.Item.ToString(), ["Rating"] = "4", ["Comment"] = new string('x', 2001), ["__RequestVerificationToken"] = Field(form) }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("at most 2000 characters", await tooLong.Content.ReadAsStringAsync());
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.Review"));

        var posted = await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["OrderItemId"] = s.Item.ToString(), ["Rating"] = "4", ["Comment"] = "Solid headphones", ["__RequestVerificationToken"] = Field(form) }));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        var after = await buyer.GetStringAsync(posted.Headers.Location!.OriginalString);
        Assert.Contains("Thanks — your review was posted.", System.Net.WebUtility.HtmlDecode(after));
        Assert.Contains("You rated this item 4 out of 5.", after);
        Assert.DoesNotContain("Thanks", System.Net.WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Orders/Details/{s.Order}")));
        var reviewed = await buyer.GetStringAsync(create);
        Assert.Contains("You rated this item 4 out of 5.", reviewed);
        Assert.DoesNotContain("How would you rate this item?", reviewed);

        var duplicate = await buyer.PostAsync("/Reviews/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["OrderItemId"] = s.Item.ToString(), ["Rating"] = "1", ["__RequestVerificationToken"] = Field(form) }));
        Assert.Equal((HttpStatusCode)409, duplicate.StatusCode);
        Assert.Contains("You already reviewed this item.", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("4|Solid headphones", SqlFixture.Scalar(s.Target, "SELECT CONCAT(rating,'|',comment) FROM dbo.Review"));
        var product = await anonymous.GetStringAsync($"/Catalog/Details/{s.Product}");
        Assert.Contains("Solid headphones", product);
        Assert.Contains("1 product rating", product);
    }
}
