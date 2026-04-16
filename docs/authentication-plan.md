# Add Windows auth + AD role-based authorization to SamReporting

## Context

The SamReporting Blazor app is an internal-only site (intranet / VPN) used by different audiences that shouldn't all see the same reports: Executives/Managers, Processors, Loan Officers, and company-wide pages that everyone can see. New audience groups will be added as new reports are built (the "Managers" group is a near-term need). Active Directory is already in use and IT can create new AD security groups on request.

**Goal:** identify every user automatically via their Windows login (no password prompt on domain PCs), then allow or deny each page based on AD group membership. No extra login screen, no separate permissions table to maintain — AD is the source of truth.

## Recommended approach

### Auth mechanism: Windows Authentication through IIS

IIS does the authentication before the request ever reaches the Blazor app. When a domain-joined PC hits the internal URL, IIS performs Kerberos/NTLM against AD and hands the Blazor app an already-authenticated `ClaimsPrincipal` that includes the user's AD group memberships as role claims. Users see no login prompt.

This works for local dev too: `dotnet watch run` picks up your Windows identity when launched from a domain-joined machine, so you can test authorization without any special dev override.

### Authorization: per-page `[Authorize(Roles = "AD-Group-Name")]`

Each Razor page declares which AD groups are allowed via the `[Authorize]` attribute. Users not in an allowed group get a 403 page. Pages that everyone should see get `[Authorize]` with no role (or are left `[AllowAnonymous]`).

The nav menu uses `<AuthorizeView Roles="…">` around each nav link so users only see links they can actually use — no 403 surprises.

**No custom permissions table.** AD group membership is the only source of truth. When HR adds someone to "SAM-Managers" in AD, they gain access to manager pages automatically on their next login. When you need a new permission tier, you ask IT to create a new group and add its name to the page's `[Authorize]` attribute.

### AD groups to create (start small, add as needed)

Work with IT to create these naming-conventioned groups:

| Group | Purpose |
|---|---|
| `SAM-Reporting-Users` | Anyone allowed to reach the site at all. Catch-all. |
| `SAM-Reporting-Executives` | Full access to everything. |
| `SAM-Reporting-Managers` | Manager-tier reports. |
| `SAM-Reporting-Processors` | Processor Pipeline + ops views. |
| `SAM-Reporting-LoanOfficers` | LO scorecards, own-pipeline views. |

More can be added later with zero code change — just add the new group name to a page's `[Authorize]` attribute.

### Page access matrix (initial)

| Page | Allowed groups |
|---|---|
| `/` (Home) | `SAM-Reporting-Users` |
| `/historical` | `SAM-Reporting-Executives`, `SAM-Reporting-Managers` |
| `/monthly` | `SAM-Reporting-Executives`, `SAM-Reporting-Managers` |
| `/processor` | `SAM-Reporting-Processors`, `SAM-Reporting-Managers`, `SAM-Reporting-Executives` |
| Future LO scorecard | `SAM-Reporting-LoanOfficers`, `SAM-Reporting-Managers`, `SAM-Reporting-Executives` |

Rule of thumb: higher tiers always inherit lower tiers' access by listing both groups.

### Site structure: keep single site, per-page auth

No need to split into multiple sites. Blazor's routing + authorization is fine-grained enough that one app with role-gated pages is simpler to maintain and deploy than separate sites. If future external-facing reports arise, *then* we'd split out a separate site.

## Files to modify

- [blazor-webapp/Program.cs](../blazor-webapp/Program.cs) — add `AddAuthentication(IISDefaults.AuthenticationScheme)`, `AddAuthorization()`, wire middleware (`app.UseAuthentication(); app.UseAuthorization();`), add `<CascadingAuthenticationState>` flow.
- [blazor-webapp/Components/Routes.razor](../blazor-webapp/Components/Routes.razor) — swap `<RouteView>` for `<AuthorizeRouteView>` so `[Authorize]` actually enforces; add `<NotAuthorized>` fallback.
- [blazor-webapp/Components/App.razor](../blazor-webapp/Components/App.razor) — wrap routes with `<CascadingAuthenticationState>`.
- [blazor-webapp/Components/_Imports.razor](../blazor-webapp/Components/_Imports.razor) — add `@using Microsoft.AspNetCore.Authorization`.
- Each page razor file — add `@attribute [Authorize(Roles = "…")]` at the top.
- [blazor-webapp/Components/Layout/MainLayout.razor](../blazor-webapp/Components/Layout/MainLayout.razor) — wrap each nav link in `<AuthorizeView Roles="…">` so hidden links don't show.
- **New file** `blazor-webapp/web.config` — ensures `<authentication mode="Windows" />` and disables anonymous when published. ASP.NET Core projects normally auto-generate this on publish, but we'll explicitly commit one so it's versioned.

## Two things your IT team does (outside the code)

1. **Create the AD groups** listed above and populate them with the right people.
2. **Enable Windows Authentication in IIS on the live server** for the SamReporting site:
   - IIS Manager → SamReporting site → Authentication → enable *Windows Authentication*, disable *Anonymous Authentication*.
   - Confirm the app pool identity has permission to query AD (the default domain service account usually does).

## What changes for the user experience

- Domain PC, on VPN: no change visible — site loads immediately with their identity recognized. Pages they can't access are hidden from the nav; if they type a URL they don't have access to, they see a "Not authorized" page.
- Non-domain PC / forgotten VPN: browser prompts for domain credentials once, then they're in for the session.

## Local development notes

- `dotnet watch run` works as-is on a domain-joined machine — Kestrel uses your current Windows identity. The authorization rules still apply, so you'll need to be in the relevant AD groups to see your own pages during dev.
- To temporarily bypass auth while iterating on a page, wrap the `[Authorize]` check in `#if !DEBUG` or add a `DevBypass` config flag. Decide later if needed — try without first.

## Verification

1. Add `[Authorize(Roles = "SAM-Reporting-Managers")]` to one existing page (pick `/monthly`).
2. Deploy to the server. Hit the URL from a PC where the test user is *not* in `SAM-Reporting-Managers` — expect a Not-Authorized screen.
3. Add the test user to that AD group. Within a few minutes (may need browser restart to refresh Kerberos ticket), the page loads normally.
4. Hit `/processor` with the same user — expect access denied since they're not in Processors.
5. Remove the user from the group — access revoked on next session.

## Out of scope for this task

- MFA / conditional access (handled by corporate VPN, not the app).
- External / public-facing access (future task; would need Entra ID or similar).
- Audit logging of who viewed what (can be added later; ASP.NET Core has request logging hooks).
- Row-level data restrictions (e.g., an LO sees only their own loans) — this plan is page-level only. Row-level filters would be a follow-up that pushes the user's identity into each SQL query's WHERE clause.
