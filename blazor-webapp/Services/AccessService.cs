using Microsoft.AspNetCore.Http;
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
    private readonly IHttpContextAccessor _http;
    private readonly IMemoryCache _cache;

    public AccessService(SqlService sql, IConfiguration config,
        IHttpContextAccessor http, IMemoryCache cache)
    {
        _sql = sql;
        _config = config;
        _http = http;
        _cache = cache;
    }

    /// <summary>
    /// Returns the username whose access we should check. Honors a DevImpersonate
    /// override from configuration (dev only) so you can test as another user
    /// without logging out.
    /// </summary>
    public string CurrentUsername()
    {
        var devUser = _config["DevImpersonate"];
        if (!string.IsNullOrWhiteSpace(devUser)) return devUser;
        return _http.HttpContext?.User.Identity?.Name ?? string.Empty;
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
