using System.ComponentModel.DataAnnotations;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EbayGroup5.Infrastructure.Authentication;

public sealed record AccountIdentity(int Id, string Name, string Role);

public sealed class AccountService(EbayDbContext db)
{
    private static readonly PasswordHasher<User> Hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 210_000
    }));
    private static readonly string DummyHash = HashPassword(Guid.NewGuid().ToString("N"));

    public static string HashPassword(string password) => Hasher.HashPassword(new User(), password);
    public static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };
    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public async Task<bool> RegisterAsync(string name, string email, string password)
    {
        name = name.Trim();
        email = Normalize(email);
        if (name.Length is < 1 or > 100 || email.Length > 100 || !new EmailAddressAttribute().IsValid(email) || !ValidPassword(password))
            return false;
        if (await db.Set<User>().AnyAsync(u => u.Email != null && u.Email.Trim().ToUpper() == email)) return false;
        var user = new User { Username = name, Email = email, Password = HashPassword(password), Role = "Buyer" };
        db.Add(user);
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<AccountIdentity?> LoginAsync(string email, string password)
    {
        if (email.Length > 100 || password.Length is < 1 or > 128) return null;
        email = Normalize(email);
        var matches = await db.Set<User>().AsNoTracking()
            .Where(u => u.Email != null && u.Email.Trim().ToUpper() == email).OrderBy(u => u.Id).Take(2).ToListAsync();
        var user = matches.Count == 1 ? matches[0] : null;
        var hash = user?.Password;
        var result = PasswordVerificationResult.Failed;
        try { result = Hasher.VerifyHashedPassword(user ?? new User(), hash ?? DummyHash, password); }
        catch (FormatException) { Hasher.VerifyHashedPassword(new User(), DummyHash, password); }
        if (hash == null || result == PasswordVerificationResult.Failed || user?.Role is not ("Buyer" or "Seller" or "Admin")) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await db.Set<User>().Where(u => u.Id == user.Id && u.Password == hash)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.Password, HashPassword(password)));
        return new AccountIdentity(user.Id, user.Username ?? "Member", user.Role);
    }
}
