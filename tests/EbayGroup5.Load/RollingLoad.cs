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
using Microsoft.Data.SqlClient;

namespace EbayGroup5.Load;

// F08: never retry an HTTP probe; reconnect/rejoin/SQL catch-up and same-UUID send retry are measured explicitly.
public static class RollingLoad
{
    private sealed record Pair(string Buyer, string Seller, int Conversation, int BuyerId, int SellerId);
    private sealed record Delivery(int Receiver, long Started);
    private sealed record Sample(string Phase, string Route, int Status, double Ms);
    private sealed class Side(HttpClient http, HubConnection hub, int me, int conversation)
    {
        public HttpClient Http { get; } = http;
        public HubConnection Hub { get; } = hub;
        public int Me { get; } = me;
        public int Conversation { get; } = conversation;
        public string Cursor { get; set; } = "0";
        public long ReconnectingAt;
        public SemaphoreSlim HistoryLock { get; } = new(1);
    }

    public static async Task<int> RunAsync(string root, string caPath, string password, string csv, int count, int workers, int seconds, string work, string output)
    {
        using var ca = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
        bool Validate(object? _, X509Certificate? cert, X509Chain? __, SslPolicyErrors errors)
        {
            if (cert == null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(cert));
        }
        var pairs = File.ReadAllLines(csv).Skip(1).Where(x => x.Length > 0).Select(x => x.Split(','))
            .Select(x => new Pair(x[0], x[1], int.Parse(x[2]), int.Parse(x[3]), int.Parse(x[4]))).Take(count).ToArray();
        if (pairs.Length != count) throw new InvalidOperationException("Insufficient rolling chat pairs.");
        var clock = Stopwatch.StartNew();
        var expected = new ConcurrentDictionary<Guid, Delivery>();
        var delivered = new ConcurrentDictionary<Guid, byte>();
        var acked = new ConcurrentDictionary<Guid, byte>();
        var latencies = new ConcurrentBag<double>();
        var recoveries = new ConcurrentBag<(string Phase, double Ms)>();
        var samples = new ConcurrentBag<Sample>();
        var sides = new List<Side>();
        var httpClients = new List<HttpClient>();
        var cookieJars = new Dictionary<HttpClient, CookieContainer>();
        var retries = 0; var failures = 0; var historyErrors = 0; var closed = 0;
        var stopping = false;
        string Phase()
        {
            try { var phase = File.ReadAllText(Path.Combine(work, "phase.txt")).Trim(); return phase.Length == 0 ? "warmup" : phase; }
            catch (IOException) { return "warmup"; }
        }
        void Receive(ChatMessageDto message, int me)
        {
            if (!expected.TryGetValue(message.ClientMessageId, out var item) || item.Receiver != me) return;
            if (delivered.TryAdd(message.ClientMessageId, 0)) latencies.Add((clock.ElapsedTicks - item.Started) * 1000.0 / Stopwatch.Frequency);
        }
        async Task<HttpClient> Login(string email)
        {
            var cookies = new CookieContainer();
            var http = new HttpClient(new HttpClientHandler
            {
                CookieContainer = cookies, AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => Validate(m, c, ch, e)
            }) { BaseAddress = new Uri(root + "/"), Timeout = TimeSpan.FromSeconds(5) };
            httpClients.Add(http);
            cookieJars[http] = cookies;
            var page = await http.GetStringAsync("Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            using var response = await http.PostAsync("Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }));
            if (response.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException("Rolling load sign-in failed.");
            return http;
        }
        async Task CatchUp(Side side)
        {
            await side.HistoryLock.WaitAsync();
            try
            {
                while (true)
                {
                    var page = await side.Http.GetFromJsonAsync<ChatHistoryPage>($"api/v1/chat/conversations/{side.Conversation}/messages?after={side.Cursor}&limit=100")
                        ?? throw new InvalidOperationException("Empty rolling chat history.");
                    foreach (var message in page.Messages) { Receive(message, side.Me); side.Cursor = message.Sequence; }
                    if (!page.HasMore) break;
                    if (page.Messages.Count == 0) throw new InvalidOperationException("History cursor did not progress.");
                }
            }
            finally { side.HistoryLock.Release(); }
        }
        async Task<Side> Connect(string email, int me, int conversation)
        {
            var http = await Login(email);
            var cookies = cookieJars[http];
            var hub = new HubConnectionBuilder().WithUrl(new Uri(root + "/hubs/chat"), options =>
            {
                options.Cookies = cookies; options.SkipNegotiation = true; options.Transports = HttpTransportType.WebSockets;
                options.Headers["Origin"] = root; options.WebSocketConfiguration = socket => socket.RemoteCertificateValidationCallback = Validate;
            }).WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) }).Build();
            var side = new Side(http, hub, me, conversation);
            hub.On<ChatMessageDto>("MessageCommitted", message => Receive(message, me));
            hub.Reconnecting += error => { Interlocked.CompareExchange(ref side.ReconnectingAt, clock.ElapsedTicks, 0); return Task.CompletedTask; };
            hub.Reconnected += async id =>
            {
                try
                {
                    await hub.InvokeAsync<ChatConversationDto>("JoinConversation", conversation);
                    await CatchUp(side);
                    var start = Interlocked.Exchange(ref side.ReconnectingAt, 0);
                    if (start > 0) recoveries.Add((Phase(), (clock.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency));
                }
                catch { Interlocked.Increment(ref failures); }
            };
            hub.Closed += error => { if (!stopping) Interlocked.Increment(ref closed); return Task.CompletedTask; };
            await hub.StartAsync();
            side.Cursor = (await hub.InvokeAsync<ChatConversationDto>("JoinConversation", conversation)).LastSequence;
            sides.Add(side);
            return side;
        }
        var pairsConnected = new List<(Side Buyer, Side Seller)>();
        try
        {
            foreach (var pair in pairs)
            {
                pairsConnected.Add((await Connect(pair.Buyer, pair.BuyerId, pair.Conversation), await Connect(pair.Seller, pair.SellerId, pair.Conversation)));
                File.WriteAllText(Path.Combine(work, "setup.json"), JsonSerializer.Serialize(new { connectedPairs = pairsConnected.Count, httpUsers = 0, elapsedSeconds = clock.Elapsed.TotalSeconds }));
            }
            var probes = new List<HttpClient>();
            for (var i = 1; i <= workers; i++)
            {
                probes.Add(await Login(LoadSeed.BuyerEmail(i)));
                File.WriteAllText(Path.Combine(work, "setup.json"), JsonSerializer.Serialize(new { connectedPairs = pairsConnected.Count, httpUsers = i, elapsedSeconds = clock.Elapsed.TotalSeconds }));
            }
            var end = clock.Elapsed + TimeSpan.FromSeconds(seconds);
            File.WriteAllText(Path.Combine(work, "ready.json"), JsonSerializer.Serialize(new { pairs = count, workers, seconds, utc = DateTime.UtcNow }));
            Console.WriteLine($"Rolling load ready: {workers} HTTP users, {count} WSS pairs, {seconds}s.");
            var httpTasks = probes.Select(async (http, index) =>
            {
                var routes = new[] { "Catalog", "api/v1/chat/conversations", $"api/v1/chat/conversations/{pairs[index % count].Conversation}/messages?limit=50" };
                // Each buyer only reads its own inbox; the history probe uses public catalog for other buyers.
                if (index >= count) routes[2] = "Orders/Purchases";
                while (clock.Elapsed < end)
                {
                    foreach (var route in routes)
                    {
                        var phase = Phase(); var begin = clock.ElapsedTicks; var status = 0;
                        try { using var response = await http.GetAsync(route); status = (int)response.StatusCode; }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) { }
                        samples.Add(new(phase, route.Split('?')[0], status, (clock.ElapsedTicks - begin) * 1000.0 / Stopwatch.Frequency));
                    }
                    await Task.Delay(500);
                }
            });
            var polling = sides.Select(async side =>
            {
                while (clock.Elapsed < end + TimeSpan.FromSeconds(5))
                {
                    try { await CatchUp(side); } catch { Interlocked.Increment(ref historyErrors); }
                    await Task.Delay(500);
                }
            });
            var sending = pairsConnected.Select(async (pair, index) =>
            {
                await Task.Delay(index * 25);
                for (var n = 0; clock.Elapsed < end; n++)
                {
                    var from = n % 2 == 0 ? pair.Buyer : pair.Seller;
                    var to = n % 2 == 0 ? pair.Seller : pair.Buyer;
                    var id = Guid.NewGuid(); expected[id] = new(to.Me, clock.ElapsedTicks);
                    var deadline = clock.Elapsed + TimeSpan.FromSeconds(10); var success = false;
                    do
                    {
                        try
                        {
                            if (from.Hub.State != HubConnectionState.Connected) { await Task.Delay(100); continue; }
                            await from.Hub.InvokeAsync<ChatSendReceipt>("SendMessage", from.Conversation, new ChatSendCommand(id, $"rolling fixture {index} {n}"));
                            acked[id] = 0; success = true; break;
                        }
                        catch { Interlocked.Increment(ref retries); await Task.Delay(100); }
                    } while (clock.Elapsed < deadline);
                    if (!success) Interlocked.Increment(ref failures);
                    await Task.Delay(900);
                }
            });
            await Task.WhenAll(httpTasks.Concat(polling).Concat(sending));
            foreach (var side in sides) await CatchUp(side);
            var missing = acked.Keys.Count(id => !delivered.ContainsKey(id));
            var sqlCounts = new Dictionary<Guid, int>();
            await using (var sql = new SqlConnection(Environment.GetEnvironmentVariable("EBAY_LOAD_SQL")))
            {
                await sql.OpenAsync();
                await using var query = sql.CreateCommand();
                query.CommandText = "SELECT ClientMessageId,COUNT(*) FROM dbo.Message WHERE ClientMessageId IN (SELECT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@ids)) GROUP BY ClientMessageId";
                query.Parameters.AddWithValue("@ids", JsonSerializer.Serialize(acked.Keys));
                await using var rows = await query.ExecuteReaderAsync();
                while (await rows.ReadAsync()) sqlCounts[rows.GetGuid(0)] = rows.GetInt32(1);
            }
            var sqlMissing = acked.Keys.Count(id => !sqlCounts.ContainsKey(id));
            var sqlDuplicates = sqlCounts.Values.Count(n => n != 1);
            var all = samples.ToArray();
            var errors = all.Count(x => x.Status != 200);
            var fiveHundreds = all.Count(x => x.Status >= 500);
            var networkErrors = all.Count(x => x.Status == 0);
            var groups = all.GroupBy(x => new { x.Phase, x.Route }).Select(g => new { g.Key.Phase, g.Key.Route, samples = g.Count(), errors = g.Count(x => x.Status != 200), p95Ms = Percentile(g.Select(x => x.Ms), .95) }).ToArray();
            var maxRecovery = recoveries.Count == 0 ? -1 : recoveries.Max(x => x.Ms);
            var report = new
            {
                workers, pairs = count, seconds, httpSamples = all.Length, httpErrors = errors, http5xx = fiveHundreds, networkErrors,
                statuses = all.GroupBy(x => x.Status).ToDictionary(g => g.Key.ToString(), g => g.Count()), routes = groups,
                acked = acked.Count, delivered = acked.Count - missing, missing, sqlMissing, sqlDuplicates,
                sendRetries = retries, finalSendErrors = failures, historyErrors, terminalClosures = closed,
                reconnects = recoveries.Count, maxRecoveryMs = maxRecovery,
                recoveries = recoveries.Select(x => new { x.Phase, x.Ms }).ToArray(), chatP95Ms = Percentile(latencies, .95)
            };
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Rolling: HTTP {all.Length}, errors={errors}, 5xx={fiveHundreds}, network={networkErrors}; ACK={acked.Count}, lost={missing}, SQL missing/duplicate={sqlMissing}/{sqlDuplicates}; reconnects={recoveries.Count}, max={maxRecovery:0.0}ms.");
            return errors == 0 && missing == 0 && sqlMissing == 0 && sqlDuplicates == 0 && failures == 0 && closed == 0 && recoveries.Count > 0 && maxRecovery <= 10000 ? 0 : 1;
        }
        finally
        {
            stopping = true;
            foreach (var side in sides) await side.Hub.DisposeAsync();
            foreach (var http in httpClients) http.Dispose();
        }
    }
    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        return sorted.Length == 0 ? -1 : Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)], 2);
    }
}
