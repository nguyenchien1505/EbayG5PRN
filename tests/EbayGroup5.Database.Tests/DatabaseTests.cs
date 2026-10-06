using DbUp.Engine;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Security.Cryptography;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Baseline_matches_original_model_roundtrips_and_preserves_existing_data()
    {
        var original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "baseline-source.sql"));
        Assert.Equal(Baseline.Sha256, Convert.ToHexString(SHA256.HashData(original)));
        var reference = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(reference);
        // Independent source extraction: original CREATE TABLE body, no runner helper or EF-generated schema.
        var text = System.Text.Encoding.UTF8.GetString(original);
        SqlFixture.Execute(reference, text[text.IndexOf("CREATE TABLE", StringComparison.Ordinal)..]);
        var expected = SqlFixture.Rows(reference, SqlFixture.Metadata);
        Assert.Equal("17", SqlFixture.Scalar(reference, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"));

        var target = SqlFixture.NewTarget();
        Assert.Equal(9, MigrationRunner.Run(target));
        var actual = SqlFixture.Rows(target, SqlFixture.Metadata);
        Assert.True(expected.IsSubsetOf(actual), "Baseline columns/identity/PK/FK/unique must remain intact.");
        var options = new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options;
        await using var db = new EbayDbContext(options);
        var expectedTables = expected.Where(row => row.StartsWith("C|")).Select(row => row.Split('|')[2]).Distinct().ToArray();
        Assert.Equal(17, expectedTables.Length);
        Assert.All(expectedTables, name => Assert.Contains(db.Model.GetEntityTypes(), entity => entity.GetTableName() == name));
        var mappedForeignKeys = new HashSet<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, "dbo");
            var columns = expected.Where(row => row.StartsWith($"C|dbo|{table.Name}|", StringComparison.Ordinal)).ToArray();
            foreach (var row in columns)
            {
                var column = row.Split('|');
                var property = Assert.Single(entity.GetProperties(), p => p.GetColumnName(table) == column[3]);
                var type = column[4] switch
                {
                    "nvarchar" => $"nvarchar({(column[5] == "-1" ? "max" : (int.Parse(column[5]) / 2).ToString())})",
                    "decimal" => $"decimal({column[6]},{column[7]})",
                    var value => value
                };
                Assert.Equal(type, property.GetColumnType());
                Assert.Equal(column[8] == "1", property.IsNullable);
                Assert.Equal(column[9] == "1", property.ValueGenerated == ValueGenerated.OnAdd);
            }
            if (columns.Length > 0)
            {
                var primary = Assert.Single(entity.FindPrimaryKey()!.Properties);
                Assert.Equal("id", primary.GetColumnName(table));
            }
            foreach (var fk in entity.GetForeignKeys())
            {
                if (fk.Properties.Count != 1) continue;
                var fkColumn = fk.Properties[0].GetColumnName(table);
                var signature = $"F|dbo|{table.Name}|{fkColumn}|dbo|{fk.PrincipalEntityType.GetTableName()}|id|0|0|0|0";
                if (expected.Contains(signature)) Assert.Equal(DeleteBehavior.ClientNoAction, fk.DeleteBehavior);
                mappedForeignKeys.Add(signature);
            }
            // Real SQL query for every mapped entity catches missing tables/columns/provider differences.
            var query = typeof(DatabaseTests).GetMethod(nameof(ReadAll), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(entity.ClrType);
            await (Task)query.Invoke(null, [db])!;
        }
        Assert.True(expected.Where(row => row.StartsWith("F|")).All(mappedForeignKeys.Contains));

        var user = new User { Username = "người mua thử", Email = "a05@example.invalid" };
        db.Add(user);
        await db.SaveChangesAsync();
        var product = new Product { Title = "Sản phẩm thử", SellerId = user.Id, Price = 123.45m, IsAuction = false, AuctionEndTime = new DateTime(2026, 10, 2, 12, 0, 0) };
        db.Add(product);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Set<Product>().SingleAsync();
        Assert.Equal(123.45m, loaded.Price);
        Assert.Equal("Sản phẩm thử", loaded.Title);
        Assert.Null(loaded.CategoryId);
        Assert.Equal(product.AuctionEndTime, loaded.AuctionEndTime);

        Assert.Equal(0, MigrationRunner.Run(target));
        Assert.True(actual.SetEquals(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal(1, await db.Set<Product>().CountAsync());
        Assert.Equal("người mua thử", (await db.Set<User>().SingleAsync()).Username);
        Assert.Equal("9", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.SchemaVersions"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(target, "INSERT dbo.[User](email) VALUES(N'a05@example.invalid')"));
        SqlFixture.Execute(target, "INSERT dbo.[User](username) VALUES(N'one null email')");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(target, "INSERT dbo.[User](username) VALUES(N'second null email')"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(target, "INSERT dbo.Product(sellerId) VALUES(-1)"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(target, $"DELETE dbo.[User] WHERE id={user.Id}"));

        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(target, login, password);
        AppAccess.Provision(target, login, password); // Repeat does not rotate/reset.
        var app = new SqlConnectionStringBuilder(target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, "INSERT dbo.Category(name) VALUES(N'app write'); UPDATE dbo.Category SET name=N'app update' WHERE name=N'app write'; DELETE dbo.Category WHERE name=N'app update';");
        Assert.Equal("0", SqlFixture.Scalar(app, "SELECT COUNT(*) FROM dbo.Category"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "CREATE TABLE dbo.Forbidden(id int)"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "INSERT dbo.DemoSeedHistory VALUES(N'forged',SYSUTCDATETIME())"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "UPDATE dbo.SchemaVersions SET ScriptName=ScriptName"));
    }

    private static async Task ReadAll<T>(EbayDbContext context) where T : class =>
        _ = await context.Set<T>().AsNoTracking().ToListAsync();

    [Fact]
    public void Upgrade_from_populated_baseline_preserves_metadata_and_data()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        Assert.True(MigrationRunner.BuildEngine(target, [new SqlScript("0001_Baseline.sql", Baseline.ReadTableDdl())]).PerformUpgrade().Successful);
        SqlFixture.Execute(target, "INSERT dbo.Category(name) VALUES(N'existing category')");
        var baseline = SqlFixture.Rows(target, SqlFixture.Metadata);
        Assert.Equal(8, MigrationRunner.Run(target));
        Assert.True(baseline.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal("existing category", SqlFixture.Scalar(target, "SELECT name FROM dbo.Category"));
        // Exercise the same transaction/journal pipeline with a genuinely additive schema change.
        Assert.True(MigrationRunner.BuildEngine(target, [new SqlScript("9997_TestExtension.sql", "ALTER TABLE dbo.Category ADD testExtension nvarchar(20) NULL;")]).PerformUpgrade().Successful);
        Assert.True(baseline.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal("existing category", SqlFixture.Scalar(target, "SELECT name FROM dbo.Category"));
        Assert.Equal("1", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.Category WHERE testExtension IS NULL"));
    }

    [Fact]
    public void Unjournaled_database_is_rejected_without_changing_existing_data()
    {
        var target = SqlFixture.NewTarget();
        SqlFixture.CreateEmpty(target);
        SqlFixture.Execute(target, "CREATE TABLE dbo.Sentinel(id int PRIMARY KEY); INSERT dbo.Sentinel VALUES(42)");
        Assert.Throws<InvalidOperationException>(() => MigrationRunner.Run(target));
        Assert.Equal("42", SqlFixture.Scalar(target, "SELECT id FROM dbo.Sentinel"));
        Assert.Equal("1", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"));
    }

    [Fact]
    public void Failed_upgrade_rolls_back_schema_data_and_journal()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.Category(name) VALUES(N'original')");
        var before = SqlFixture.Rows(target, SqlFixture.Metadata);
        var engine = MigrationRunner.BuildEngine(target,
        [new SqlScript("9998_TestAddition.sql", "ALTER TABLE dbo.Category ADD testProbe int NULL; UPDATE dbo.Category SET name=N'changed';"),
         new SqlScript("9999_TestFailure.sql", "THROW 51000, 'Expected test failure', 1;")]);
        Assert.False(engine.PerformUpgrade().Successful);
        Assert.True(before.SetEquals(SqlFixture.Rows(target, SqlFixture.Metadata)));
        Assert.Equal("original", SqlFixture.Scalar(target, "SELECT name FROM dbo.Category"));
        Assert.Equal("9", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.SchemaVersions"));
    }

    [Fact]
    public async Task Concurrent_runners_apply_each_script_once()
    {
        var target = SqlFixture.NewTarget();
        var results = await Task.WhenAll(Task.Run(() => MigrationRunner.Run(target)), Task.Run(() => MigrationRunner.Run(target)));
        Assert.Equal(new[] { 0, 9 }, results.Order().ToArray());
        Assert.Equal("9", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.SchemaVersions"));
    }

    [Theory]
    [InlineData("master")]
    [InlineData("CloneEbayDB")]
    [InlineData("EbayGroup5]; SELECT 1;--")]
    public void Unexpected_target_is_refused(string database) =>
        Assert.Throws<ArgumentException>(() => MigrationRunner.ValidateDatabaseName(database));
}
