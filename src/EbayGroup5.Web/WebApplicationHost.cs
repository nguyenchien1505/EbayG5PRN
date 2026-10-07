using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EbayGroup5.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authorization;
using EbayGroup5.Web.Chat;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EbayGroup5.Web;

public static class WebApplicationHost
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<EbayGroup5.Application.Chat.IChatService, EbayGroup5.Infrastructure.Chat.ChatService>();
        builder.Services.AddScoped<ChatApiFilter>();
        builder.Services.AddSingleton<ChatOriginPolicy>();
        builder.Services.AddScoped<ChatDeliveryGuard>();
        builder.Services.AddScoped<ChatRealtime>();
        builder.Services.AddScoped<ChatHubFilter>();
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = EbayGroup5.Application.Chat.ChatLimits.MaxBodyBytes;
            options.MaximumParallelInvocationsPerClient = 1;
            options.AddFilter<ChatHubFilter>();
        }).AddStackExchangeRedis(options =>
        {
            var connection = builder.Configuration.GetConnectionString("Redis");
            var prefix = builder.Configuration["Chat:RedisChannelPrefix"];
            if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(prefix) || prefix.Length > 128 ||
                prefix.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '.' or '-' or '_')))
                throw new InvalidOperationException("Chat Redis configuration is required.");
            try { options.Configuration = ConfigurationOptions.Parse(connection); }
            catch { throw new InvalidOperationException("Invalid chat Redis configuration."); }
            options.Configuration.AbortOnConnectFail = false;
            options.Configuration.ChannelPrefix = RedisChannel.Literal(prefix);
            options.Configuration.AsyncTimeout = 2000;
            options.Configuration.ConnectTimeout = 3000;
            options.Configuration.ConnectRetry = 1;
        });
        builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        // F04: ASP.NET's default API encoder is "relaxed" and leaves < > & ' " raw; escape them so JSON can never be read as markup.
        builder.Services.Configure<JsonOptions>(options =>
            options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All));
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            var defaultFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context => context.HttpContext.Request.Path.StartsWithSegments("/api/v1/chat")
                ? new BadRequestObjectResult(new ProblemDetails { Status = 400, Title = "Invalid chat request." })
                : defaultFactory(context);
        });
        builder.Services.AddScoped<EbayGroup5.Application.Orders.IOrderService, EbayGroup5.Infrastructure.Orders.OrderService>();
        builder.Services.AddScoped<EbayGroup5.Application.Reviews.IReviewService, EbayGroup5.Infrastructure.Reviews.ReviewService>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<EbayGroup5.Application.Feedback.IFeedbackService, EbayGroup5.Infrastructure.Reputation.FeedbackService>();
        builder.Services.AddScoped<EbayGroup5.Application.Moderation.IModerationService, EbayGroup5.Infrastructure.Moderation.ModerationService>();
        // E01: business-day timing; Disputes:DemoBusinessDaySeconds > 0 shortens deadlines for demos (DISPUTES.md).
        builder.Services.AddSingleton(new EbayGroup5.Application.Disputes.DisputeTiming(
            builder.Configuration.GetSection("Disputes").Get<EbayGroup5.Application.Disputes.DisputeTimingOptions>()));
        builder.Services.AddScoped<EbayGroup5.Application.Disputes.IDisputeService, EbayGroup5.Infrastructure.Disputes.DisputeService>();
        // E04: overdue System commands and automatic notices on every replica (DISPUTE_AUTOMATION.md); bound lazily so hosts/tests can override.
        builder.Services.AddOptions<EbayGroup5.Application.Disputes.DisputeAutomationOptions>().Bind(builder.Configuration.GetSection("Disputes:Automation"));
        builder.Services.AddScoped<EbayGroup5.Application.Disputes.IDisputeAutomation, EbayGroup5.Infrastructure.Disputes.DisputeAutomationService>();
        builder.Services.AddHostedService<EbayGroup5.Web.Disputes.DisputeAutomationWorker>();
        builder.Services.AddScoped<EbayGroup5.Application.Checkout.ICheckoutService, EbayGroup5.Infrastructure.Checkout.CheckoutService>();
        builder.Services.AddScoped<EbayGroup5.Application.Catalog.ICatalogService, EbayGroup5.Infrastructure.Catalog.CatalogService>();
        builder.Services.AddScoped<AccessCookieEvents>();
        builder.Services.AddSingleton<AdminNetwork>();
        builder.Services.AddSingleton<ReadinessProbe>();
        // F02 (SCALE_OUT.md): one Redis connection for readiness, shared rate limits and (in deployments) the key ring.
        builder.Services.AddSingleton<RedisConnection>();
        builder.Services.AddSingleton<SharedRateLimiter>();
        builder.Services.AddDataProtection().SetApplicationName("EbayGroup5");
        builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
            .Configure<RedisConnection, IConfiguration>((options, redis, configuration) =>
            {
                // Every instance reads and writes the same key ring, so a cookie from one instance is valid on all of them.
                if (configuration["DataProtection:KeyStore"] == "Redis")
                    options.XmlRepository = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                        () => redis.Multiplexer.GetDatabase(), redis.KeyPrefix + ":DataProtection-Keys");
            });
        builder.Services.AddSingleton<IAuthorizationHandler, AccountOwnerHandler>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "EbayGroup5.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.LoginPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.EventsType = typeof(AccessCookieEvents);
        });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AccountOwner", policy => policy.RequireAuthenticatedUser().AddRequirements(new AccountOwnerRequirement()));
            options.AddPolicy("Buyer", policy => policy.RequireRole("Buyer"));
            options.AddPolicy("Seller", policy => policy.RequireRole("Seller"));
            options.AddPolicy("AdminDemo", policy => policy.RequireRole("Admin"));
        });
        builder.Services.AddDbContext<EbayDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is required for database operations.")));

        var app = builder.Build();
        // F02: fail at start-up on a misconfigured key store (later it would only surface as 500s) or rate-limit policy.
        if (app.Configuration["DataProtection:KeyStore"] is { Length: > 0 } store && store != "Redis")
            throw new InvalidOperationException($"Unsupported DataProtection:KeyStore '{store}'.");
        app.Services.GetRequiredService<SharedRateLimiter>();

        // F01: behind Nginx, client IP and scheme come from the proxy's headers, and only from configured proxy addresses.
        if (ReverseProxy.Options(app.Configuration) is { } forwarded) app.UseForwardedHeaders(forwarded);
        // F04 (SECURITY_TESTS.md): CSP/nosniff/frame headers everywhere; HSTS only over HTTPS for real host names (ASP.NET skips localhost).
        app.UseSecurityHeaders();
        if (!app.Environment.IsDevelopment()) app.UseHsts();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1/chat") || context.Request.Path.StartsWithSegments("/hubs/chat"))
                context.Response.OnStarting(() => { context.Response.Headers.CacheControl = "no-store"; return Task.CompletedTask; });
            await next(context);
        });

        app.UseExceptionHandler(handler => handler.Run(context =>
                Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpected error occurred.").ExecuteAsync(context)));

        app.UseStaticFiles();
        app.UseWhen(context => HttpMethods.IsGet(context.Request.Method) && !context.Request.Path.StartsWithSegments("/api/v1/chat") && !context.Request.Path.StartsWithSegments("/hubs/chat"), branch =>
            branch.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}"));
        app.UseRouting();
        app.UseAuthentication();
        // F02: shared counters (Redis) right after authentication, before antiforgery and MVC (SCALE_OUT.md).
        app.UseSharedRateLimits();
        app.Use(async (context, next) =>
        {
            if (context.User.IsInRole("Admin") && !context.RequestServices.GetRequiredService<AdminNetwork>().Allows(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });
        app.UseAuthorization();
        app.UseWebSockets();

        var origins = app.Services.GetRequiredService<ChatOriginPolicy>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs/chat"))
            {
                if (!origins.Allows(context.Request.Headers.Origin.ToString())) { context.Response.StatusCode = 403; return; }
                if (context.Request.Query.Keys.Any(x => x.Equals("access_token", StringComparison.OrdinalIgnoreCase))) { context.Response.StatusCode = 400; return; }
                if (context.Request.Path != "/hubs/chat" || !context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            }
            await next(context);
        });

        // Liveness: the process answers. Readiness (F01): SQL Server and Redis are reachable; no details are returned.
        app.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));
        app.MapGet("/health/ready", async (HttpContext context, ReadinessProbe probe, EbayDbContext db) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return await probe.ReadyAsync(db, context.RequestAborted)
                ? Results.Text("Ready", "text/plain")
                : Results.Text("Not ready", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        app.MapControllers();
        app.MapHub<ChatHub>("/hubs/chat", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
            options.ApplicationMaxBufferSize = EbayGroup5.Application.Chat.ChatLimits.MaxBodyBytes;
            options.TransportMaxBufferSize = 65536;
        });
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");


        return app;
    }
}
