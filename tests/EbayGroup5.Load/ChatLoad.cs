using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EbayGroup5.Load;

/// <summary>
/// F05 realtime measurement (LOAD.md): every pair has one SignalR WebSocket per side through nginx (WSS), both sides alternate sending
/// about once a second, and the receiving side records send -> MessageCommitted latency with the same Stopwatch (one process, one clock).
/// </summary>
public static class ChatLoad
{
    private sealed record Pair(string BuyerEmail, string SellerEmail, int ConversationId, int BuyerId, int SellerId);
    private sealed record Sent(long StartedTicks, int Pair);

    public static async Task<int> RunAsync(string baseUrl, string caPath, string password, string pairsCsv, int count, int seconds, string output)
    {
        var ca = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
        bool Validate(object? _, X509Certificate? certificate, X509Chain? __, SslPolicyErrors errors)
        {
            if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(certificate));
        }
        var pairs = File.ReadAllLines(pairsCsv).Skip(1).Where(x => x.Length > 0).Select(x => x.Split(','))
            .Select(x => new Pair(x[0], x[1], int.Parse(x[2]), int.Parse(x[3]), int.Parse(x[4]))).Take(count).ToList();
        if (pairs.Count < count) throw new ArgumentException($"Only {pairs.Count} chat pairs available.");

        var clock = Stopwatch.StartNew();
        var pending = new ConcurrentDictionary<Guid, Sent>();
        var latencies = new ConcurrentBag<double>();
        var received = new ConcurrentDictionary<Guid, int>();
        var duplicates = 0;
        var sendErrors = 0;
        string? firstError = null;
        var connections = new List<HubConnection>();
        var clients = new List<HttpClient>();

        async Task<HubConnection> Connect(string email, int me, int pair)
        {
            var cookies = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (m, c, ch, e) => Validate(m, c, ch, e) };
            var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
            clients.Add(http);
            var page = await http.GetStringAsync("Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var login = await http.PostAsync("Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }));
            if (login.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException($"Sign-in failed for a load user ({(int)login.StatusCode}).");
            var hub = new HubConnectionBuilder().WithUrl(new Uri(baseUrl + "/hubs/chat"), options =>
            {
                options.Cookies = cookies;
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                options.Headers["Origin"] = baseUrl;
                options.WebSocketConfiguration = socket => socket.RemoteCertificateValidationCallback = Validate;
            }).Build();
            hub.On<ChatMessageDto>("MessageCommitted", message =>
            {
                if (message.SenderId == me) return;
                if (!received.TryAdd(message.ClientMessageId, pair)) { Interlocked.Increment(ref duplicates); return; }
                if (pending.TryRemove(message.ClientMessageId, out var sent))
                    latencies.Add((clock.ElapsedTicks - sent.StartedTicks) * 1000.0 / Stopwatch.Frequency);
            });
            await hub.StartAsync();
            connections.Add(hub);
            return hub;
        }

        // Connect sequentially (buyer then seller of each pair) so nginx spreads sockets over the replicas round-robin.
        var sides = new List<(HubConnection Buyer, HubConnection Seller, Pair Pair)>();
        foreach (var (pair, index) in pairs.Select((p, i) => (p, i)))
        {
            var buyer = await Connect(pair.BuyerEmail, pair.BuyerId, index);
            var seller = await Connect(pair.SellerEmail, pair.SellerId, index);
            await buyer.InvokeAsync<ChatConversationDto>("JoinConversation", pair.ConversationId);
            await seller.InvokeAsync<ChatConversationDto>("JoinConversation", pair.ConversationId);
            sides.Add((buyer, seller, pair));
        }
        Console.WriteLine($"Chat load: {sides.Count} pairs, {connections.Count} WebSockets joined; sending for {seconds}s");

        var sentCount = 0;
        // The sending window starts once every socket has joined (sign-ins and connects are not part of it).
        var end = clock.Elapsed + TimeSpan.FromSeconds(seconds);
        await Task.WhenAll(sides.Select(async (side, index) =>
        {
            var random = new Random(index);
            await Task.Delay(random.Next(0, 1000));
            for (var n = 0; clock.Elapsed < end; n++)
            {
                var from = n % 2 == 0 ? side.Buyer : side.Seller;
                var id = Guid.NewGuid();
                pending[id] = new(clock.ElapsedTicks, index);
                try
                {
                    await from.InvokeAsync<ChatSendReceipt>("SendMessage", side.Pair.ConversationId, new ChatSendCommand(id, $"load chat pair {index} message {n}"));
                    Interlocked.Increment(ref sentCount);
                }
                catch (Exception ex)
                {
                    pending.TryRemove(id, out _);
                    Interlocked.Increment(ref sendErrors);
                    Interlocked.CompareExchange(ref firstError, $"{ex.GetType().Name}: {ex.Message}", null);
                }
                await Task.Delay(800 + random.Next(0, 400));
            }
        }));
        // Let in-flight messages arrive.
        var settle = Stopwatch.StartNew();
        while (!pending.IsEmpty && settle.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(100);

        var sorted = latencies.OrderBy(x => x).ToArray();
        double Percentile(double p) => sorted.Length == 0 ? -1 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
        var report = new Dictionary<string, object>
        {
            ["pairs"] = sides.Count, ["webSockets"] = connections.Count, ["seconds"] = seconds,
            ["sent"] = sentCount, ["received"] = sorted.Length, ["missing"] = pending.Count, ["duplicates"] = duplicates, ["sendErrors"] = sendErrors,
            ["p50Ms"] = Math.Round(Percentile(0.50), 1), ["p95Ms"] = Math.Round(Percentile(0.95), 1), ["p99Ms"] = Math.Round(Percentile(0.99), 1),
            ["maxMs"] = Math.Round(sorted.LastOrDefault(), 1), ["firstSendError"] = firstError ?? "",
            ["userIds"] = pairs.SelectMany(x => new[] { x.BuyerId, x.SellerId }).ToArray(),
            ["pairUsers"] = pairs.Select(x => new[] { x.BuyerId, x.SellerId }).ToArray(),
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (firstError != null) Console.WriteLine("Chat load first send error: " + firstError);
        Console.WriteLine($"Chat load: sent {sentCount}, received {sorted.Length}, missing {pending.Count}, duplicates {duplicates}, send errors {sendErrors}, " +
            $"p50 {Percentile(0.50):0.0} ms, p95 {Percentile(0.95):0.0} ms, p99 {Percentile(0.99):0.0} ms, max {sorted.LastOrDefault():0.0} ms");
        foreach (var hub in connections) await hub.DisposeAsync();
        foreach (var http in clients) http.Dispose();
        return 0;
    }
}
