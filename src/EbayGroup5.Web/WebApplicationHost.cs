using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EbayGroup5.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using EbayGroup5.Web.Security;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;
using EbayGroup5.Web.Chat;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using StackExchange.Redis;

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
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            var defaultFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context => context.HttpContext.Request.Path.StartsWithSegments("/api/v1/chat")
                ? new BadRequestObjectResult(new ProblemDetails { Status = 400, Title = "Invalid chat request." })
                : defaultFactory(context);
        });
        builder.Services.AddScoped<EbayGroup5.Application.Orders.IOrderService, EbayGroup5.Infrastructure.Orders.OrderService>();
        builder.Services.AddScoped<EbayGroup5.Application.Checkout.ICheckoutService, EbayGroup5.Infrastructure.Checkout.CheckoutService>();
        builder.Services.AddScoped<EbayGroup5.Application.Catalog.ICatalogService, EbayGroup5.Infrastructure.Catalog.CatalogService>();
        builder.Services.AddScoped<AccessCookieEvents>();
        builder.Services.AddSingleton<AdminNetwork>();
        builder.Services.AddSingleton<IAuthorizationHandler, AccountOwnerHandler>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("Auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
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
        app.UseRateLimiter();
        app.UseAuthentication();
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

        // Process liveness only. Database/Redis readiness is added with infrastructure.
        app.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));
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
