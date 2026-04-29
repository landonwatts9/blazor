using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace SamReporting.Services;

/// <summary>
/// Resolves the current Windows user (or a DevImpersonate override in dev),
/// then queries dbo.DashboardAccess to determine which dashboards they may see.
/// Used by the home page, the nav menu, and each dashboard's per-page guard.
/// </summary>
public class AccessService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const string Sql =
        @"SELECT dashboard_key FROM dbo.DashboardAccess WHERE username = @user";

    private readonly SqlService _sql;
    private readonly IConfiguration _config;
    private readonly AuthenticationStateProvider _authState;
    private readonly IMemoryCache _cache;

    public AccessService(SqlService sql, IConfiguration config,
        AuthenticationStateProvider authState, IMemoryCache cache)
    {
        _sql = sql;
        _config = config;
        _authState = authState;
        _cache = cache;
    }

    /// <summary>
    /// Returns the username whose access we should check. Honors a DevImpersonate
    /// override from configuration (dev only) so you can test as another user
    /// without logging out. Uses Blazor's AuthenticationStateProvider so it
    /// works both during the initial HTTP request and inside the SignalR
    /// circuit (where HttpContext is null).
    /// </summary>
    public async Task<string> CurrentUsernameAsync()
    {
        var devUser = _config["DevImpersonate"];
        if (!string.IsNullOrWhiteSpace(devUser)) return devUser;

        var state = await _authState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? string.Empty;
    }

    /// <summary>Returns the set of dashboard keys this user is allowed to view.</summary>
    public Task<HashSet<string>> GetAllowedDashboardsAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Task.FromResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return _cache.GetOrCreateAsync($"access:{username}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var rows = await _sql.QueryAsync(Sql, ("@user", username));
            return rows
                .Select(r => (string)r["dashboard_key"]!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        })!;
    }

    public async Task<bool> CanAccessAsync(string username, string dashboardKey) =>
        (await GetAllowedDashboardsAsync(username)).Contains(dashboardKey);
}
