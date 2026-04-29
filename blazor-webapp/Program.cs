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

        // Windows authentication. AddNegotiate fails to initialize under
        // an IIS app pool identity (it tries to perform its own Kerberos
        // setup), so we use it only for Kestrel/dev. Under IIS hosting the
        // IIS in-process server auto-registers a handler for the IIS scheme;
        // we just need to point both DefaultScheme and DefaultChallengeScheme
        // at it so authorization knows what to challenge with.
        if (builder.Environment.IsDevelopment())
        {
            builder.Services
                .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                .AddNegotiate();
        }
        else
        {
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IISDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = IISDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = IISDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = IISDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = IISDefaults.AuthenticationScheme;
                options.DefaultSignOutScheme = IISDefaults.AuthenticationScheme;
            });
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
