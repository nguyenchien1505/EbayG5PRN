using System.Net;
using EbayGroup5.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;

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

/// <summary>F01 readiness: SQL Server and Redis reachable (shared F02 connection); no details leave the process.</summary>
public sealed class ReadinessProbe(RedisConnection redis)
{
    public async Task<bool> ReadyAsync(EbayDbContext db, CancellationToken ct)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) return false;
            var connection = redis.Multiplexer;
            if (!connection.IsConnected) return false;
            await connection.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
