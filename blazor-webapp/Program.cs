using ApexCharts;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
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

        // Windows authentication. Under IIS, the IIS module performs the
        // Kerberos/NTLM handshake and forwards the resulting identity — we
        // just register the IIS scheme. Under Kestrel (dotnet watch run on
        // a domain-joined dev machine), Negotiate handles it directly.
        if (builder.Environment.IsDevelopment())
        {
            builder.Services
                .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                .AddNegotiate();
        }
        else
        {
            builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);
        }

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
