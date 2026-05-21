# Dashboard access control: Windows auth + per-user SQL permissions + adaptive home page

> Supersedes the broader [authentication-plan.md](authentication-plan.md) (which proposed AD-group-based roles). This plan keeps the Windows-auth half but replaces AD groups with a simple per-user SQL table.

## Context

The site is currently open to anyone who can reach it — every domain user sees every link. We need to gate dashboards so each person only sees the ones they're explicitly granted, without making them remember another password. The site is internal (intranet/VPN) on an AD-joined IIS server, so silent Windows authentication is available "for free" once the IIS role service is installed.

**Approach in three pieces:**

1. **Windows Authentication** identifies the user — IIS does the AD handshake, hands the app a `ClaimsPrincipal` with the username (e.g. `SAM\landon.watts`). No login screen.
2. A SQL **`DashboardAccess` table** holds per-user grants: one row per `(username, dashboard_key)`. Edited via SSMS for now.
3. A C# **`DashboardCatalog`** static registry lists every dashboard's metadata (key, title, description, route). The home page filters this list by what the SQL table says the current user can access, rendering only those cards.

Per-user-only granularity (no roles for now). Admin happens via direct SQL edits — no admin UI yet. Default for users with no rows in the table: a friendly "no access — contact landon.watts@sunamerican.com" page.

## Pre-flight: install Windows Authentication on the IIS server

Windows Authentication is an optional Windows role service that isn't installed by default — it didn't appear in the IIS Authentication module on the live server because the role service was missing.

1. Open **Server Manager** on the IIS server (Start → "Server Manager").
2. Top-right **Manage** → **Add Roles and Features**.
3. Through the wizard: defaults → role-based → this server.
4. **Server Roles** → expand **Web Server (IIS) → Web Server → Security** → check **Windows Authentication**.
5. Skip Features, click **Install**, wait for completion.
6. Open **IIS Manager**, refresh — the **Authentication** module on the site should now list **Windows Authentication** alongside Anonymous, ASP.NET Impersonation, and Forms Authentication.

## SQL: permissions table

Run once in SSMS targeting `SAM_Reporting`:

```sql
CREATE TABLE dbo.DashboardAccess (
    username       NVARCHAR(200) NOT NULL,   -- 'SAM\landon.watts' (DOMAIN\sAMAccountName)
    dashboard_key  NVARCHAR(50)  NOT NULL,   -- matches DashboardCatalog keys: 'monthly','historical','processor'
    granted_at     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    granted_by     NVARCHAR(200) NULL,       -- who added this row, for audit
    notes          NVARCHAR(500) NULL,
    CONSTRAINT PK_DashboardAccess PRIMARY KEY (username, dashboard_key)
);
CREATE INDEX IX_DashboardAccess_username ON dbo.DashboardAccess (username);
```

Seed with your own access so you can test:

```sql
INSERT INTO dbo.DashboardAccess (username, dashboard_key, granted_by) VALUES
    ('SAM\landon.watts', 'monthly',    'SAM\landon.watts'),
    ('SAM\landon.watts', 'historical', 'SAM\landon.watts'),
    ('SAM\landon.watts', 'processor',  'SAM\landon.watts');
```

The script will be saved at `blazor-webapp/sql/2026-04-17_create_dashboard_access.sql` during implementation.

## App changes

### `Program.cs`

```csharp
using Microsoft.AspNetCore.Authentication.Negotiate;

builder.Services
    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;     // every page authenticated by default
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AccessService>();

// ... after app.UseStaticFiles(); app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
```

### `Components/App.razor`

Wrap `<Routes />` so authorization state cascades to every component:

```razor
<CascadingAuthenticationState>
    <Routes />
</CascadingAuthenticationState>
```

### `Components/Routes.razor`

Replace `<RouteView>` with `<AuthorizeRouteView>` and add a `<NotAuthorized>` fallback so the URL-typing case is handled:

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <NoAccessMessage />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

### New `Services/DashboardCatalog.cs`

Static registry — one entry per dashboard, single source of truth for the home page and any future audit views:

```csharp
public record DashboardEntry(string Key, string Title, string Description, string Route);

public static class DashboardCatalog
{
    public static readonly DashboardEntry Monthly = new(
        "monthly", "Monthly Dashboard",
        "MTD funded volume, projected pipeline by channel, turn times, LO summary.",
        "/monthly");

    public static readonly DashboardEntry Historical = new(
        "historical", "Historical Production",
        "Historical funded production analysis with prior-period comparisons.",
        "/historical");

    public static readonly DashboardEntry ProcessorPipeline = new(
        "processor", "Processor Pipeline",
        "My Pipeline loans by underwriting status.",
        "/processor");

    public static readonly IReadOnlyList<DashboardEntry> All =
        new[] { Monthly, Historical, ProcessorPipeline };
}
```

### New `Services/AccessService.cs`

The single check used by the home page, the nav menu, and the per-page guards. Includes the dev-impersonation override:

```csharp
public class AccessService
{
    private const string Sql = @"SELECT dashboard_key FROM dbo.DashboardAccess WHERE username = @user";
    private readonly SqlService _sql;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;

    public AccessService(SqlService sql, IConfiguration config, IHttpContextAccessor http)
    { _sql = sql; _config = config; _http = http; }

    public string CurrentUsername()
    {
        // Dev-only override: appsettings.Development.json sets DevImpersonate to a username.
        var devUser = _config["DevImpersonate"];
        if (!string.IsNullOrWhiteSpace(devUser)) return devUser;
        return _http.HttpContext?.User.Identity?.Name ?? "";
    }

    public async Task<HashSet<string>> GetAllowedDashboardsAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return new();
        var rows = await _sql.QueryAsync(Sql, ("@user", username));
        return rows.Select(r => (string)r["dashboard_key"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> CanAccessAsync(string username, string dashboardKey) =>
        (await GetAllowedDashboardsAsync(username)).Contains(dashboardKey);
}
```

### Rewrite `Components/Pages/Home.razor`

Becomes the adaptive landing — shows only the cards the user has access to:

```razor
@page "/"
@inject AccessService Access

<h1>SAM Reporting</h1>
<p class="subtitle">Welcome, @username.</p>

@if (allowed is null)
{
    <p>Loading…</p>
}
else if (allowed.Count == 0)
{
    <NoAccessMessage />
}
else
{
    <div class="dashboard-grid">
        @foreach (var d in DashboardCatalog.All.Where(d => allowed.Contains(d.Key)))
        {
            <a class="dashboard-card" href="@d.Route">
                <h3>@d.Title</h3>
                <p>@d.Description</p>
            </a>
        }
    </div>
}

@code {
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }
    private HashSet<string>? allowed;
    private string username = "";

    protected override async Task OnInitializedAsync()
    {
        username = Access.CurrentUsername();
        allowed = await Access.GetAllowedDashboardsAsync(username);
    }
}
```

### New `Components/Shared/NoAccessMessage.razor`

Reused by the home page (no rows) and the per-page NotAuthorized fallback (URL typed directly):

```razor
<div class="no-access">
    <h2>You don't have access to that dashboard</h2>
    <p>Contact <a href="mailto:landon.watts@sunamerican.com">landon.watts@sunamerican.com</a> to request access.</p>
</div>
```

### Per-dashboard guard

Each dashboard page gets a tiny check at the top of `OnInitializedAsync` (or guards rendering until access is confirmed):

```razor
@inject AccessService Access
@code {
    private bool authorized;
    protected override async Task OnInitializedAsync()
    {
        authorized = await Access.CanAccessAsync(Access.CurrentUsername(), "monthly");
        if (!authorized) return;
        // ... existing data load
    }
}

@if (!authorized) { <NoAccessMessage /> }
else { /* existing markup */ }
```

Done for `MonthlyDashboard.razor`, `Historical.razor`, and `ProcessorPipeline.razor` with the matching catalog key.

### Adaptive nav in `MainLayout.razor`

Replace the static link list with an iteration over the catalog filtered by access. Pulls from the same `AccessService` so home page and nav stay consistent.

## IIS configuration on the live server

After Windows Authentication is installed:

1. **IIS Manager → Default Web Site (or your site) → Authentication**:
   - **Anonymous Authentication** → right-click → **Disable**.
   - **Windows Authentication** → right-click → **Enable**.
2. Recycle the app pool. Hit the URL — you should see no login prompt; the page loads as you.

## Local dev / impersonation

`appsettings.Development.json` (gitignored, so it stays local):

```json
{
  "DevImpersonate": "SAM\\some.other.user"
}
```

When this key is non-empty, `AccessService.CurrentUsername()` returns it instead of the real Windows identity. Lets you test as Bob without logging out. Keep it blank or remove the key in `appsettings.json` (committed) so production never impersonates.

## Files to add

- `blazor-webapp/sql/2026-04-17_create_dashboard_access.sql` — table + seed.
- `blazor-webapp/Services/DashboardCatalog.cs`
- `blazor-webapp/Services/AccessService.cs`
- `blazor-webapp/Components/Shared/NoAccessMessage.razor` (+ optional `.razor.css`)

## Files to modify

- `blazor-webapp/Program.cs` — auth/authz/cascading state/AccessService/HttpContextAccessor.
- `blazor-webapp/Components/App.razor` — wrap with `<CascadingAuthenticationState>`.
- `blazor-webapp/Components/Routes.razor` — `AuthorizeRouteView` + `<NotAuthorized>`.
- `blazor-webapp/Components/Pages/Home.razor` — adaptive landing.
- `blazor-webapp/Components/Layout/MainLayout.razor` — adaptive nav.
- `blazor-webapp/Components/Pages/MonthlyDashboard.razor`, `Historical.razor`, `ProcessorPipeline.razor` — access guard.
- `blazor-webapp/SamReporting.csproj` — add `Microsoft.AspNetCore.Authentication.Negotiate` package.
- `blazor-webapp/appsettings.json` (`DevImpersonate: ""`) and gitignored `appsettings.Development.json`.

## Verification

1. After deploy, visit the site as a domain user. No login prompt; page renders.
2. With your AD account in `DashboardAccess` for all three keys → home page shows three cards; nav shows three links; all three dashboard URLs load.
3. Remove your `monthly` row, refresh → home page shows two cards, `/monthly` URL shows "No access".
4. Have a coworker who isn't in the table hit the home page → they see the NoAccessMessage with the contact email.
5. Locally: set `DevImpersonate` to a coworker's domain account → home page renders as if you were them. Clear the setting → reverts to your own identity.
6. Add a fourth dashboard later: register it in `DashboardCatalog.cs` and grant rows in SQL. No other changes needed for the home page or nav to pick it up.

## Out of scope (next time)

- Admin UI for editing `DashboardAccess` (add a `/admin/access` page once non-developers need to manage rows).
- Role abstraction (group people into "Manager"/"Processor" sets so onboarding is one INSERT instead of N).
- Audit log of grant/revoke actions beyond the `granted_by` / `granted_at` columns.
- Row-level data restrictions (e.g., LO sees only their own loans within a dashboard).
- Public / external access (would need Entra ID / OAuth instead of Windows auth).
