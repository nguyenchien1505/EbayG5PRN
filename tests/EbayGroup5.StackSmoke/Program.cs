// F01 smoke test (docs/architecture/DEPLOYMENT.md): checks a running stack through Nginx only, trusting just the given CA.
// Usage: EbayGroup5.StackSmoke --https-port N --http-port N --ca path/to/ca.crt [--wait-seconds 240]
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

var options = Args(args);
var httpsPort = int.Parse(options["--https-port"]);
var httpPort = int.Parse(options["--http-port"]);
var ca = X509Certificate2.CreateFromPem(File.ReadAllText(options["--ca"]));
var wait = TimeSpan.FromSeconds(int.Parse(options.GetValueOrDefault("--wait-seconds", "240")));
var origin = $"https://localhost:{httpsPort}";
var failures = 0;

// Only certificates chaining to the test CA with a matching name pass; nothing is skipped.
bool Validate(object? _, X509Certificate? certificate, X509Chain? __, SslPolicyErrors errors)
{
    if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) return false;
    using var chain = new X509Chain();
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(ca);
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    return chain.Build(new X509Certificate2(certificate));
}

var cookies = new CookieContainer();
using var handler = new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (m, c, ch, e) => Validate(m, c, ch, e) };
using var https = new HttpClient(handler) { BaseAddress = new Uri(origin + "/"), Timeout = TimeSpan.FromSeconds(20) };
using var plainHandler = new HttpClientHandler { AllowAutoRedirect = false };
using var http = new HttpClient(plainHandler) { BaseAddress = new Uri($"http://localhost:{httpPort}/"), Timeout = TimeSpan.FromSeconds(20) };

void Check(bool ok, string name)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failures++;
}

// 1. Wait for the stack (migrate -> web ready -> nginx) without trusting anything but the CA.
var deadline = DateTime.UtcNow + wait;
HttpResponseMessage? ready = null;
while (DateTime.UtcNow < deadline)
{
    try
    {
        ready = await https.GetAsync("health/ready");
        if (ready.StatusCode == HttpStatusCode.OK) break;
    }
    catch (HttpRequestException) { }
    await Task.Delay(2000);
}
Check(ready?.StatusCode == HttpStatusCode.OK && await ready.Content.ReadAsStringAsync() == "Ready", "HTTPS /health/ready returns Ready (TLS chain and name verified against the local CA)");
if (failures > 0) return 1;

var live = await https.GetAsync("health/live");
Check(live.StatusCode == HttpStatusCode.OK && await live.Content.ReadAsStringAsync() == "Healthy", "HTTPS /health/live");
Check(live.Headers.TryGetValues("Server", out var server) && server.All(x => x == "nginx"), "Server header hides the nginx version");
Check(ready!.Headers.CacheControl?.NoStore == true, "Readiness is not cached");

// 2. Plain HTTP only redirects to the same path on HTTPS.
var redirect = await http.GetAsync("Account/Login?returnUrl=%2FMessages");
Check(redirect.StatusCode == HttpStatusCode.PermanentRedirect && redirect.Headers.Location?.ToString() == $"{origin}/Account/Login?returnUrl=%2FMessages",
    "HTTP 308 redirects to the HTTPS URL");

// 3. Register and sign in over HTTPS; the auth cookie is Secure, HttpOnly and SameSite=Lax.
string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
var id = Guid.NewGuid().ToString("N")[..12];
var email = $"stack-{id}@example.invalid";
var password = "Stack-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
var register = await https.PostAsync("Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["Name"] = "Stack smoke " + id, ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Token(await https.GetStringAsync("Account/Register")) }));
Check(register.StatusCode == HttpStatusCode.Redirect, "Register over HTTPS");
var login = await https.PostAsync("Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Token(await https.GetStringAsync("Account/Login")) }));
var authCookie = login.Headers.TryGetValues("Set-Cookie", out var setCookies) ? setCookies.FirstOrDefault(x => x.StartsWith("EbayGroup5.Auth=", StringComparison.Ordinal)) : null;
Check(login.StatusCode == HttpStatusCode.Redirect && authCookie != null, "Login over HTTPS sets the auth cookie");
var flags = authCookie?.ToLowerInvariant() ?? "";
Check(flags.Contains("; secure") && flags.Contains("; httponly") && flags.Contains("samesite=lax"), "Auth cookie is Secure, HttpOnly and SameSite=Lax");
var me = await https.GetAsync("Account/Me");
Check(me.StatusCode == HttpStatusCode.OK && (await me.Content.ReadAsStringAsync()).Contains("Stack smoke " + id), "Signed-in page over HTTPS");

// 4. Chat over WSS through nginx: SignalR JSON handshake with the right Origin; a foreign Origin is refused.
async Task<string> Handshake(string requestOrigin)
{
    using var socket = new ClientWebSocket();
    socket.Options.RemoteCertificateValidationCallback = Validate;
    socket.Options.SetRequestHeader("Origin", requestOrigin);
    socket.Options.SetRequestHeader("Cookie", cookies.GetCookieHeader(new Uri(origin)));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await socket.ConnectAsync(new Uri($"wss://localhost:{httpsPort}/hubs/chat"), timeout.Token);
    await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
    var buffer = new byte[1024];
    var received = await socket.ReceiveAsync(buffer, timeout.Token);
    var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    return text;
}
try { Check(await Handshake(origin) == "{}\u001e", "WSS /hubs/chat SignalR handshake through nginx"); }
catch (Exception ex) { Check(false, "WSS /hubs/chat SignalR handshake through nginx (" + ex.GetType().Name + ")"); }
try { await Handshake("https://attacker.example"); Check(false, "Foreign Origin is refused on WSS"); }
catch (WebSocketException) { Check(true, "Foreign Origin is refused on WSS"); }

// 5. Client-supplied X-Forwarded-For is overwritten by nginx: varied spoofed addresses still share one rate-limit partition.
var limited = false;
var loginPage = await https.GetStringAsync("Account/Login");
for (var i = 0; i < 12 && !limited; i++)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "Account/Login")
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = email, ["Password"] = "wrong-password-123", ["__RequestVerificationToken"] = Token(loginPage) })
    };
    request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"198.51.100.{i + 1}");
    limited = (await https.SendAsync(request)).StatusCode == HttpStatusCode.TooManyRequests;
}
Check(limited, "Spoofed X-Forwarded-For is ignored (auth rate limit still applies per real client)");

Console.WriteLine(failures == 0 ? "PASS: stack smoke over HTTPS/WSS" : $"FAIL: {failures} stack smoke check(s)");
return failures == 0 ? 0 : 1;

static Dictionary<string, string> Args(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i + 1 < args.Length; i += 2) map[args[i]] = args[i + 1];
    foreach (var required in new[] { "--https-port", "--http-port", "--ca" })
        if (!map.ContainsKey(required)) throw new ArgumentException($"Missing {required}.");
    return map;
}
