var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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
