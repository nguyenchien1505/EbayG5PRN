using Microsoft.Data.SqlClient;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class DemoSeedTests
{
    [Fact]
    public async Task Seed_is_consistent_concurrent_and_preserves_user_changes_on_rerun()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.[User](email,username) VALUES(N'existing@example.invalid',N'Existing'); INSERT dbo.Category(name) VALUES(N'Electronics');");
        var results = await Task.WhenAll(Task.Run(() => DemoSeedRunner.Run(target)), Task.Run(() => DemoSeedRunner.Run(target)));
        Assert.Single(results, applied => applied);
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.[User]"));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.[User] WHERE password IS NULL AND role IN ('Buyer','Seller','Admin')"));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Product p JOIN dbo.Inventory i ON p.id=i.productId WHERE i.quantity>0 AND p.isAuction=0"));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(DISTINCT status) FROM dbo.OrderTable WHERE status IN ('Pending','Shipped','Delivered','Cancelled')"));
        Assert.Equal("8", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderItem"));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderTable WHERE SellerId IS NOT NULL"));
        // G01: seeded orders carry the 0011 estimated delivery date (7 calendar days, weekend-adjusted), so item-not-received can open.
        Assert.Equal("2026-09-25|2026-09-28|2026-09-29|2026-09-30", SqlFixture.Scalar(target,
            "SELECT STRING_AGG(CONVERT(char(10), EstimatedDeliveryUtc, 23), '|') WITHIN GROUP (ORDER BY id) FROM dbo.OrderTable"));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.OrderTable o WHERE o.totalPrice<>(SELECT SUM(quantity*unitPrice) FROM dbo.OrderItem WHERE orderId=o.id)"));
        Assert.Equal("4", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Payment p JOIN dbo.OrderTable o ON o.id=p.orderId WHERE p.amount=o.totalPrice AND p.userId=o.buyerId AND p.method='COD' AND ((o.status='Delivered' AND p.status='Paid' AND p.paidAt IS NOT NULL) OR (o.status<>'Delivered' AND p.paidAt IS NULL AND p.status=CASE WHEN o.status='Cancelled' THEN 'Cancelled' ELSE 'Pending' END))"));
        Assert.Equal("Existing", SqlFixture.Scalar(target, "SELECT username FROM dbo.[User] WHERE email='existing@example.invalid'"));
        SqlFixture.Execute(target, "UPDATE dbo.Product SET price=99; UPDATE dbo.[User] SET username=N'Edited' WHERE email='buyer.a06@example.invalid'; DELETE dbo.ShippingInfo;");
        Assert.False(DemoSeedRunner.Run(target));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Product WHERE price=99"));
        Assert.Equal("Edited", SqlFixture.Scalar(target, "SELECT username FROM dbo.[User] WHERE email='buyer.a06@example.invalid'"));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.ShippingInfo"));
        Assert.Equal("2", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.DemoSeedHistory"));
    }

    [Fact]
    public void Image_step_upgrades_an_older_seed_once_and_keeps_user_changed_images()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        Assert.True(DemoSeedRunner.Run(target));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Product WHERE images LIKE N'[[]\"/img/products/%'"));
        // Database seeded before H05: v1 journal only, images NULL; one listing already edited by a user.
        SqlFixture.Execute(target, "DELETE dbo.DemoSeedHistory WHERE Version=N'H05-v1'; UPDATE dbo.Product SET images=NULL; UPDATE dbo.Product SET images=N'[]' WHERE title=N'Desk Lamp';");
        Assert.True(DemoSeedRunner.Run(target));
        Assert.Equal("[]", SqlFixture.Scalar(target, "SELECT images FROM dbo.Product WHERE title=N'Desk Lamp'"));
        Assert.Equal("[\"/img/products/headphones-1.svg\",\"/img/products/headphones-2.svg\"]", SqlFixture.Scalar(target, "SELECT images FROM dbo.Product WHERE title=N'Wireless Headphones'"));
        Assert.Equal("3", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Product"));
        Assert.False(DemoSeedRunner.Run(target));
    }

    [Fact]
    public void Email_collision_fails_without_adopting_or_overwriting_existing_user()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.[User](email,username,role) VALUES(N'seller.a06@example.invalid',N'User owned',N'Buyer');");
        Assert.Throws<SqlException>(() => DemoSeedRunner.Run(target));
        Assert.Equal("User owned", SqlFixture.Scalar(target, "SELECT username FROM dbo.[User]"));
        Assert.Equal("1", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.[User]"));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.DemoSeedHistory"));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Product"));
    }

    [Fact]
    public void Failure_after_inserts_rolls_back_data_and_journal_and_can_retry()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        // A test-only fault late in the batch proves rollback of earlier user/product/order writes.
        SqlFixture.Execute(target, "CREATE TRIGGER dbo.SeedFault ON dbo.Payment AFTER INSERT AS THROW 51009, 'Test fault', 1;");
        Assert.Throws<SqlException>(() => DemoSeedRunner.Run(target));
        foreach (var table in new[] { "User", "Product", "Category", "OrderTable", "OrderItem", "Inventory", "Address", "Store", "Payment", "DemoSeedHistory" })
            Assert.Equal("0", SqlFixture.Scalar(target, $"SELECT COUNT(*) FROM dbo.[{table}]"));
        SqlFixture.Execute(target, "DISABLE TRIGGER dbo.SeedFault ON dbo.Payment;");
        Assert.True(DemoSeedRunner.Run(target));
        Assert.Equal("2", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.DemoSeedHistory"));
    }

    [Fact]
    public void Seed_requires_initialized_database_and_rejects_other_targets()
    {
        Assert.Throws<ArgumentException>(() => DemoSeedRunner.Run("Server=unused;Database=Other"));
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        Assert.Throws<SqlException>(() => DemoSeedRunner.Run(target));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM sys.tables"));
    }
}
