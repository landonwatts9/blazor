using ApexCharts;
using Microsoft.AspNetCore.Authentication.Negotiate;
using SamReporting.Components;
using SamReporting.Services;

namespace SamReporting;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Windows authentication. AddNegotiate registers the actual handler
        // (so authorization has a scheme to challenge with) and works in
        // both Kestrel (dev) and IIS hosting — under IIS the handler defers
        // to the identity already established by the IIS module.
        builder.Services
            .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();

        // Require authentication on every page by default. Pages can opt out
        // with [AllowAnonymous] if needed (none currently do).
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = options.DefaultPolicy;
        });

        builder.Services.AddCascadingAuthenticationState();

        builder.Services.AddSingleton<SqlService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<HistoricalService>();
        builder.Services.AddScoped<ProcessorService>();
        builder.Services.AddScoped<MonthlyDashboardService>();
        builder.Services.AddScoped<OriginationsService>();
        builder.Services.AddScoped<AccessService>();
        builder.Services.AddApexCharts();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();

        // Order matters: authentication must run before authorization, and
        // authorization must run before antiforgery (which inspects the user).
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
