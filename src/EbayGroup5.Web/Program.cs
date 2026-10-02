using EbayGroup5.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<EbayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required for database operations.")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(handler => handler.Run(context =>
        Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.").ExecuteAsync(context)));
}

app.UseStaticFiles();
app.UseRouting();

// Process liveness only. Database/Redis readiness is added with infrastructure.
app.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
