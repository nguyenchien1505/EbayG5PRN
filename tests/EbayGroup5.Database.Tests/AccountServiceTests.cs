using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class AccountServiceTests
{
    private static EbayDbContext Context(string target) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(target).Options);

    [Fact]
    public async Task Registration_normalizes_hashes_assigns_buyer_and_rejects_concurrent_duplicates()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        const string password = "Test-Only-Password-785!";
        async Task<bool> Register()
        {
            await using var db = Context(target);
            return await new AccountService(db).RegisterAsync("  Example  ", " buyer@example.invalid ", password);
        }
        var results = await Task.WhenAll(Register(), Register());
        Assert.Single(results, success => success);
        await using var context = Context(target);
        var service = new AccountService(context);
        var user = await context.Set<User>().SingleAsync();
        Assert.Equal("Buyer", user.Role);
        Assert.Equal("Example", user.Username);
        Assert.Equal("BUYER@EXAMPLE.INVALID", user.Email);
        Assert.NotEqual(password, user.Password);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(user, user.Password!, password));
        var login = await service.LoginAsync(" Buyer@Example.Invalid ", password);
        Assert.Equal(user.Id, login!.Id);
        Assert.Null(await service.LoginAsync(user.Email!, "wrong-password"));
        Assert.False(await service.RegisterAsync("Name", "bad-email", password));
        Assert.False(await service.RegisterAsync("Name", "short@example.invalid", "short"));
        Assert.False(await service.RegisterAsync(" ", "blank@example.invalid", password));
    }

    [Fact]
    public async Task Null_malformed_unknown_role_and_missing_accounts_cannot_login()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        await using var db = Context(target);
        var service = new AccountService(db);
        Assert.Null(await service.LoginAsync("buyer.a06@example.invalid", "Test-Only-Password-785!"));
        db.Add(new User { Email = "bad@example.invalid", Password = "not a hash!", Role = "Buyer" });
        db.Add(new User { Email = "role@example.invalid", Password = AccountService.HashPassword("Test-Only-Password-785!"), Role = "Root" });
        await db.SaveChangesAsync();
        Assert.Null(await service.LoginAsync("bad@example.invalid", "Test-Only-Password-785!"));
        Assert.Null(await service.LoginAsync("role@example.invalid", "Test-Only-Password-785!"));
        Assert.Null(await service.LoginAsync("missing@example.invalid", "Test-Only-Password-785!"));
    }

    [Fact]
    public async Task Demo_credentials_enable_once_preserve_passwords_and_rollback_changed_identity()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target);
        DemoSeedRunner.Run(target);
        var passwords = new Dictionary<string, string> { ["Buyer"] = "Buyer-Test-Only-123!", ["Seller"] = "Seller-Test-Only-123!", ["Admin"] = "Admin-Test-Only-123!" };
        SqlFixture.Execute(target, "UPDATE dbo.[User] SET username=N'Changed' WHERE role='Admin'");
        Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() => DemoCredentials.Enable(target, passwords));
        Assert.Equal("0", SqlFixture.Scalar(target, "SELECT COUNT(*) FROM dbo.[User] WHERE password IS NOT NULL"));
        SqlFixture.Execute(target, "UPDATE dbo.[User] SET username=N'Demo Admin' WHERE role='Admin'");
        Assert.Equal(3, DemoCredentials.Enable(target, passwords));
        var before = SqlFixture.Rows(target, "SELECT password FROM dbo.[User]");
        Assert.Equal(0, DemoCredentials.Enable(target, passwords));
        Assert.True(before.SetEquals(SqlFixture.Rows(target, "SELECT password FROM dbo.[User]")));
        await using var db = Context(target);
        foreach (var pair in passwords)
            Assert.Equal(pair.Key, (await new AccountService(db).LoginAsync(pair.Key.ToLowerInvariant()+".a06@example.invalid", pair.Value))!.Role);
        Assert.False(DemoSeedRunner.Run(target));
        Assert.True(before.SetEquals(SqlFixture.Rows(target, "SELECT password FROM dbo.[User]")));
    }
}
