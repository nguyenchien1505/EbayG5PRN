using System.Collections.Concurrent;
using System.Security.Claims;
using StackExchange.Redis;

namespace EbayGroup5.Web.Security;

/// <summary>F02 (SCALE_OUT.md): one lazily created Redis connection for readiness, shared rate limits and the key ring.</summary>
public sealed class RedisConnection(IConfiguration configuration) : IAsyncDisposable
{
    private readonly Lazy<ConnectionMultiplexer> connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis is not configured."));
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = Math.Min(options.ConnectTimeout, 3000);
        options.AsyncTimeout = Math.Min(options.AsyncTimeout, 2000);
        options.ConnectRetry = 1;
        return ConnectionMultiplexer.Connect(options);
    });

    public IConnectionMultiplexer Multiplexer => connection.Value;

    /// <summary>Prefix for every key this app writes; each environment (and each test host) uses its own.</summary>
    public string KeyPrefix { get; } = configuration["Redis:KeyPrefix"] is { Length: > 0 and <= 128 } prefix ? prefix : "EbayGroup5";

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated) await connection.Value.DisposeAsync();
    }
}

/// <summary>Marks an endpoint for a shared rate-limit policy; enforced by <see cref="SharedRateLimitMiddleware"/> before MVC filters.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SharedRateLimitAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}

public sealed record RateLimitDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>
/// Fixed-window counters in Redis shared by every instance. If Redis is unavailable each instance falls back to its own
/// in-memory window: still limited, just not shared (never fail-open, never lock users out because Redis is down).
/// </summary>
public sealed class SharedRateLimiter
{
    public const string Auth = "auth", ChatApi = "chat-api", ChatHub = "chat-hub";
    private static readonly IReadOnlyDictionary<string, (int Limit, int Seconds)> Defaults = new Dictionary<string, (int, int)>
    {
        [Auth] = (10, 60), [ChatApi] = (120, 60), [ChatHub] = (120, 60)
    };
    // INCR and set the expiry on the first hit in one atomic step.
    private const string Script = "local c = redis.call('INCR', KEYS[1]) if c == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end return c";

    private readonly RedisConnection redis;
    private readonly TimeProvider clock;
    private readonly ILogger<SharedRateLimiter> logger;
    private readonly Dictionary<string, (int Limit, int Seconds)> policies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Window, int Count)> local = new(StringComparer.Ordinal);
    private long lastWarning;

    public SharedRateLimiter(RedisConnection redis, IConfiguration configuration, TimeProvider clock, ILogger<SharedRateLimiter> logger)
    {
        this.redis = redis;
        this.clock = clock;
        this.logger = logger;
        foreach (var (name, (limit, seconds)) in Defaults)
        {
            var section = configuration.GetSection("RateLimits:" + name);
            var permit = section.GetValue("PermitLimit", limit);
            var window = section.GetValue("WindowSeconds", seconds);
            if (permit is < 1 or > 10_000 || window is < 1 or > 3600) throw new InvalidOperationException($"Invalid RateLimits:{name} configuration.");
            policies[name] = (permit, window);
        }
    }

    public async Task<RateLimitDecision> AcquireAsync(string policy, string partition)
    {
        if (!policies.TryGetValue(policy, out var settings)) throw new ArgumentException("Unknown rate-limit policy.", nameof(policy));
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var windowMs = settings.Seconds * 1000L;
        var window = now / windowMs;
        var retryAfter = TimeSpan.FromMilliseconds(Math.Max(1000, (window + 1) * windowMs - now));
        var key = $"{redis.KeyPrefix}:rl:{policy}:{window}:{partition}";
        long count;
        try
        {
            var connection = redis.Multiplexer;
            if (!connection.IsConnected) throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is not connected.");
            count = (long)await connection.GetDatabase().ScriptEvaluateAsync(Script, [key], [windowMs * 2]);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TimeoutException or InvalidOperationException)
        {
            Warn();
            count = local.AddOrUpdate(policy + "|" + partition, (window, 1), (_, current) => current.Window == window ? (window, current.Count + 1) : (window, 1)).Count;
            if (local.Count > 50_000) Prune(window);
        }
        return new(count <= settings.Limit, retryAfter);
    }

    private void Prune(long window)
    {
        foreach (var (key, value) in local)
            if (value.Window < window) local.TryRemove(key, out _);
    }

    private void Warn()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref lastWarning);
        if (now - last < 60_000 || Interlocked.CompareExchange(ref lastWarning, now, last) != last) return;
        logger.LogWarning("Shared rate limits unavailable (Redis); using per-instance limits until Redis is back.");
    }

    /// <summary>User id for signed-in callers, otherwise the client address (already the real client behind the configured proxy).</summary>
    public static string Partition(HttpContext context, string policy) =>
        policy != Auth && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } user
            ? "u:" + user
            : "ip:" + (context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown");
}

/// <summary>Runs after authentication and before antiforgery/MVC, so rejected requests (even without a token) are counted.</summary>
public static class SharedRateLimitMiddleware
{
    public static IApplicationBuilder UseSharedRateLimits(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SharedRateLimitAttribute>() is not { } limit) { await next(context); return; }
        var limiter = context.RequestServices.GetRequiredService<SharedRateLimiter>();
        var decision = await limiter.AcquireAsync(limit.Policy, SharedRateLimiter.Partition(context, limit.Policy));
        if (decision.Allowed) { await next(context); return; }
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api/v1/chat"))
            await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests.").ExecuteAsync(context);
    });
}
