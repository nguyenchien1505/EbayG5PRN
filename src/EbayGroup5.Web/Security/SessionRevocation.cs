using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using StackExchange.Redis;

namespace EbayGroup5.Web.Security;

/// <summary>
/// F04 / Q-SEC-001 (SECURITY_TESTS.md): sessions revoked at logout, shared by every instance through Redis. Each sign-in carries a
/// random session id; a copied cookie stops working once its session logs out. If Redis is unavailable the revoking instance still
/// remembers the session locally (degrade, never lock users out), and other instances learn nothing until the cookie expires.
/// </summary>
public sealed class SessionRevocation(RedisConnection redis, TimeProvider clock, ILogger<SessionRevocation> logger)
{
    public const string ClaimType = "eg5:sid";
    private readonly ConcurrentDictionary<string, DateTimeOffset> local = new(StringComparer.Ordinal);
    private long lastWarning;

    public static Claim NewClaim() => new(ClaimType, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());

    /// <summary>The session id of a principal, or null when it is missing or malformed (such cookies are rejected).</summary>
    public static string? SessionId(ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimType) is { Length: 32 } sid && sid.All(char.IsAsciiHexDigitLower) ? sid : null;

    private RedisKey Key(string sid) => $"{redis.KeyPrefix}:revoked-session:{sid}";

    public async Task RevokeAsync(string sid, DateTimeOffset expiresUtc)
    {
        var now = clock.GetUtcNow();
        if (expiresUtc <= now) return;
        foreach (var (key, until) in local) if (until <= now) local.TryRemove(key, out _);
        local[sid] = expiresUtc;
        try { await redis.Multiplexer.GetDatabase().StringSetAsync(Key(sid), "1", expiresUtc - now); }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException) { Warn(); }
    }

    public async Task<bool> IsRevokedAsync(string sid)
    {
        if (local.TryGetValue(sid, out var until) && until > clock.GetUtcNow()) return true;
        try { return await redis.Multiplexer.GetDatabase().KeyExistsAsync(Key(sid)); }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException) { Warn(); return false; }
    }

    private void Warn()
    {
        // At most one warning a minute; never logs the session id.
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var last = Interlocked.Read(ref lastWarning);
        if (now - last >= 60 && Interlocked.CompareExchange(ref lastWarning, now, last) == last)
            logger.LogWarning("Session revocation list unavailable (Redis); revocations stay local to this instance until Redis is back.");
    }
}
