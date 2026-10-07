using System.Net;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;

namespace EbayGroup5.Web.Security;

/// <summary>F01 (DEPLOYMENT.md): forwarded headers only from configured proxies; never from arbitrary clients.</summary>
public static class ReverseProxy
{
    /// <summary>
    /// Returns null when no proxy is configured. ASP.NET trusts every sender when both KnownProxies and KnownNetworks are empty,
    /// so the middleware must not run at all in that case.
    /// </summary>
    public static ForwardedHeadersOptions? Options(IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0) return null;
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = false
        };
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var proxy in proxies)
            options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address) ? address : throw new InvalidOperationException("Invalid ReverseProxy:KnownProxies entry."));
        foreach (var network in networks)
        {
            var parts = network.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length) || length < 0 ||
                length > (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
                throw new InvalidOperationException("Invalid ReverseProxy:KnownNetworks entry.");
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
        }
        return options;
    }
}

/// <summary>F01 readiness: SQL Server and Redis reachable. A dedicated, lazily created Redis connection; no details leave the process.</summary>
public sealed class ReadinessProbe(IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ConnectionMultiplexer? redis;

    public async Task<bool> ReadyAsync(EbayDbContext db, CancellationToken ct)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) return false;
            var connection = await Redis(ct);
            await connection.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<ConnectionMultiplexer> Redis(CancellationToken ct)
    {
        if (redis is { IsConnected: true } ready) return ready;
        await gate.WaitAsync(ct);
        try
        {
            if (redis is { IsConnected: true } existing) return existing;
            if (redis != null) await redis.DisposeAsync();
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis is not configured."));
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 3000;
            options.ConnectRetry = 1;
            redis = await ConnectionMultiplexer.ConnectAsync(options);
            return redis;
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (redis != null) await redis.DisposeAsync();
        gate.Dispose();
    }
}
