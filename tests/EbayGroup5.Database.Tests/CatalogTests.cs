using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Catalog;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class CatalogTests
{
    [Fact]
    public async Task Search_filters_pages_and_detail_use_valid_fixed_price_listings()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);
        var service = new CatalogService(db);
        var initial = await service.SearchAsync(null, null, 1);
        Assert.Equal(3, initial.Total);
        Assert.Equal(2, initial.Categories.Count);
        var headphones = Assert.Single((await service.SearchAsync(" Headphones ", null, 1)).Items);
        Assert.Equal(20, (await service.FindAsync(headphones.Id))!.Quantity);
        Assert.Equal(2, (await service.SearchAsync(null, headphones.CategoryId, 1)).Total);
        Assert.Empty((await service.SearchAsync("%", null, 1)).Items);
        Assert.Empty((await service.SearchAsync(null, int.MaxValue, 1)).Items);
        Assert.Null(await service.FindAsync(int.MaxValue));
        for (var i = 0; i < 14; i++)
            db.Add(new Product { Title = "Page item " + i, Price = 10, IsAuction = false, SellerId = headphones.SellerId });
        var auction = new Product { Title = "Excluded auction", Price = 10, IsAuction = true, SellerId = headphones.SellerId };
        db.AddRange(auction, new Product { Title = "Missing price", IsAuction = false, SellerId = headphones.SellerId },
            new Product { Title = "Missing seller", Price = 10, IsAuction = false });
        await db.SaveChangesAsync();
        var first = await service.SearchAsync("Page item", null, 1);
        var second = await service.SearchAsync("Page item", null, 2);
        Assert.Equal(14, first.Total);
        Assert.Equal(12, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Empty((await service.SearchAsync("Page item", null, 3)).Items);
        Assert.Equal(0, (await service.FindAsync(first.Items[0].Id))!.Quantity);
        Assert.Null(await service.FindAsync(auction.Id));
        Assert.Equal(17, (await service.SearchAsync(null, null, 1)).Total);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(null, null, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(new string('x', 101), null, 1));
    }
}
