using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EbayGroup5.Application.Chat;
using EbayGroup5.Web;
using EbayGroup5.Web.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Xunit;

namespace EbayGroup5.Database.Tests;

internal sealed class ChatSocketHost(WebApplication app, Uri address) : IAsyncDisposable
{
    public const string Origin = "http://127.0.0.1:5085";
    public Uri Address { get; } = address;
    private readonly List<SocketClient> sockets = [];
    public static async Task<ChatSocketHost> Start(ChatFixture fixture, IDataProtectionProvider keys, string? redis = null, int cookieSeconds = 1800,
        Action<IServiceCollection>? customize = null, string? address = null, ILoggerProvider? logs = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development",
            ContentRootPath = Path.Combine(root.FullName, "src", "EbayGroup5.Web"), ApplicationName = typeof(AccountController).Assembly.GetName().Name });
        builder.WebHost.UseUrls(address ?? "http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (logs != null) builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = fixture.Target,
            ["ConnectionStrings:Redis"] = redis ?? Environment.GetEnvironmentVariable("EBAY_TEST_REDIS") ?? throw new InvalidOperationException("Run Test-CI.ps1 or Test-Database.ps1; Redis is required."),
            ["Chat:RedisChannelPrefix"] = "EbayGroup5.Tests." + new SqlConnectionStringBuilder(fixture.Target).InitialCatalog,
            ["Chat:AllowedOrigins:0"] = Origin,
            ["Chat:AllowedOrigins:1"] = "http://localhost:5085"
        });
        builder.Services.AddSingleton(keys);
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, options => options.ExpireTimeSpan = TimeSpan.FromSeconds(cookieSeconds));
        customize?.Invoke(builder.Services);
        var app = WebApplicationHost.Build(builder);
        try
        {
            await app.StartAsync();
            var boundAddress = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new(boundAddress));
        }
        catch { await app.DisposeAsync(); throw; }
    }
    public async Task<SocketClient> Login(int index)
    {
        var cookies = new CookieContainer();
        var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = Address };
        var html = await http.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = $"chat{index}@example.invalid", ["Password"] = ChatFixture.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var csrf = await http.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/chat/antiforgery");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!["requestToken"]);
        var client = new SocketClient(this, http, cookies);
        sockets.Add(client);
        return client;
    }
    public HttpClient Anonymous() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Address };
    public Task Stop() => app.StopAsync();
    public async ValueTask DisposeAsync()
    {
        foreach (var client in sockets) await client.DisposeAsync();
        await app.StopAsync(); await app.DisposeAsync();
    }
}

internal sealed class SocketClient(ChatSocketHost host, HttpClient http, CookieContainer cookies) : IAsyncDisposable
{
    public HttpClient Http { get; } = http;
    public HubConnection? Hub { get; private set; }
    public Channel<ChatMessageDto> Messages { get; } = Channel.CreateUnbounded<ChatMessageDto>();
    public Channel<ChatReadReceipt> Reads { get; } = Channel.CreateUnbounded<ChatReadReceipt>();
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task Connect(string? origin = ChatSocketHost.Origin, string path = "/hubs/chat")
    {
        Hub = new HubConnectionBuilder().WithUrl(new Uri(host.Address, path), options =>
        {
            options.Cookies = cookies; options.SkipNegotiation = true; options.Transports = HttpTransportType.WebSockets;
            if (origin != null) options.Headers["Origin"] = origin;
        }).Build();
        Hub.On<ChatMessageDto>("MessageCommitted", message => Messages.Writer.TryWrite(message));
        Hub.On<ChatReadReceipt>("ReadAdvanced", read => Reads.Writer.TryWrite(read));
        Hub.Closed += _ => { Closed.TrySetResult(); return Task.CompletedTask; };
        await Hub.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }
    public async Task<int> Open(int product)
    {
        var response = await Http.PostAsJsonAsync("/api/v1/chat/conversations", new { productId = product });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChatOpenResult>())!.Conversation.Id;
    }
    public async Task<ChatConversationDto> Join(int id) => await Hub!.InvokeAsync<ChatConversationDto>("JoinConversation", id).WaitAsync(TimeSpan.FromSeconds(10));
    public async Task<ChatSendReceipt> Send(int id, ChatSendCommand command) => await Hub!.InvokeAsync<ChatSendReceipt>("SendMessage", id, command).WaitAsync(TimeSpan.FromSeconds(10));
    public async Task<ChatMessageDto> NextMessage() => await Messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    public async Task<ChatReadReceipt> NextRead() => await Reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    public async Task AssertQuiet(bool reads = false)
    {
        // Use an ordered invocation completion barrier where possible, then a bounded negative-event window.
        await Task.Delay(250);
        Assert.False(reads ? Reads.Reader.TryPeek(out _) : Messages.Reader.TryPeek(out _));
    }
    public async ValueTask DisposeAsync() { if (Hub != null) await Hub.DisposeAsync(); Http.Dispose(); }
}
