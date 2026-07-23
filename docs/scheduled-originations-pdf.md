# Scheduled Originations PDF

Runbook for the weekly PDF export of the Originations dashboard.

## What this is

Every Saturday at 12:00 PM local time, DASHBOARD generates a single-page PDF of
the [/originations/print](../blazor-webapp/Components/Pages/OriginationsPrint.razor)
view, drops it into an OneDrive-synced SharePoint folder, and a Power Automate
flow watching that folder emails the report to a recipient list.

## Architecture

```
Task Scheduler (Saturday 12:00 PM, runs as SUNAMERICAN\landon.watts)
        │
        ▼
C:\Reports\Capture-Originations.ps1
        │
        ├── Headless Edge → captures /originations/print
        │       → writes PDF to %TEMP%\SamReporting\Weekly_Originations-<utc>.pdf
        │
        ├── Move-Item → moves PDF to:
        │       C:\Users\landon.watts\OneDrive - Sun American Mortgage Company\
        │       Accounting - originations_marketing\
        │
        ▼
OneDrive sync client
        │
        ▼
SharePoint: sunamericanmortgage.sharepoint.com/sites/Accounting/
            Shared Documents/dashboard_exports/originations_marketing
        │
        ▼
Power Automate flow → email to recipients
```

## Key files and locations

| Thing | Location |
|---|---|
| Print view route | `/originations/print` on the live dashboard |
| Print view source | [blazor-webapp/Components/Pages/OriginationsPrint.razor](../blazor-webapp/Components/Pages/OriginationsPrint.razor) |
| Print view styles | [blazor-webapp/Components/Pages/OriginationsPrint.razor.css](../blazor-webapp/Components/Pages/OriginationsPrint.razor.css) |
| Print layout wrapper | [blazor-webapp/Components/Layout/PrintLayout.razor](../blazor-webapp/Components/Layout/PrintLayout.razor) |
| Capture script (source of truth) | [scripts/Capture-Originations.ps1](../scripts/Capture-Originations.ps1) |
| Capture script (runtime copy) | `C:\Reports\Capture-Originations.ps1` on DASHBOARD |
| Redeploy helper | [scripts/Redeploy.ps1](../scripts/Redeploy.ps1) |
| Scheduled task name | `Originations Weekly PDF` |
| OneDrive-synced folder | `C:\Users\landon.watts\OneDrive - Sun American Mortgage Company\Accounting - originations_marketing\` |
| SharePoint destination | [Accounting site / dashboard_exports / originations_marketing](https://sunamericanmortgage.sharepoint.com/sites/Accounting) |

## Change the schedule

All commands run in an elevated PowerShell on DASHBOARD.

### Change the time or day

```powershell
# Different time (e.g., 6 AM instead of noon)
Set-ScheduledTask -TaskName "Originations Weekly PDF" `
    -Trigger (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Saturday -At 6:00am)

# Multiple days per week
Set-ScheduledTask -TaskName "Originations Weekly PDF" `
    -Trigger (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday,Saturday -At 12:00pm)

# Daily
Set-ScheduledTask -TaskName "Originations Weekly PDF" `
    -Trigger (New-ScheduledTaskTrigger -Daily -At 6:30am)
```

### Pause the schedule (vacation, freeze, etc.)

```powershell
Disable-ScheduledTask -TaskName "Originations Weekly PDF"
# ... later, resume ...
Enable-ScheduledTask -TaskName "Originations Weekly PDF"
```

### Delete the schedule

```powershell
Unregister-ScheduledTask -TaskName "Originations Weekly PDF" -Confirm:$false
```

### Recreate the schedule from scratch

Use the block below (equivalent to what was originally set up):

```powershell
$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument '-NoProfile -ExecutionPolicy Bypass -File "C:\Reports\Capture-Originations.ps1"'

$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Saturday -At 12:00pm

$principal = New-ScheduledTaskPrincipal `
    -UserId "$env:USERDOMAIN\landon.watts" `
    -LogonType Interactive `
    -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 15)

Register-ScheduledTask `
    -TaskName "Originations Weekly PDF" `
    -Description "Captures /originations/print and drops the PDF into OneDrive-synced SharePoint." `
    -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

## Run the report on demand

```powershell
# Uses "current period" defaults (this month, current-week Saturday)
& C:\Reports\Capture-Originations.ps1

# Or fire the scheduled task directly (same effect, but goes through
# Task Scheduler so it also updates LastRunTime/LastTaskResult)
Start-ScheduledTask -TaskName "Originations Weekly PDF"
```

For regenerating a **prior week's** report (e.g., re-send last week's PDF),
pass filter overrides:

```powershell
& C:\Reports\Capture-Originations.ps1 -Year 2026 -Month 7 -WeekEndingOverride "2026-07-19"
```

## Deploy code changes

Any change to the Blazor app OR to `scripts/Capture-Originations.ps1` — after
the commit lands on `main` in GitHub — is picked up on DASHBOARD by running
[Redeploy.ps1](../scripts/Redeploy.ps1) in an elevated PowerShell:

```powershell
& C:\inetpub\wwwroot\blazor\scripts\Redeploy.ps1
```

That single command does the whole sequence:

1. `git pull origin main` (skip with `-SkipPull` if code is already in place)
2. Drops `app_offline.htm` into the publish folder so IIS gracefully shuts
   down the worker and releases DLL locks
3. Runs `dotnet publish -c Release`
4. Copies `scripts\Capture-Originations.ps1` into `C:\Reports\` (skip with
   `-SkipCaptureScript`)
5. Removes `app_offline.htm` so the site comes back up

The whole sequence is wrapped in `try/finally` so the offline marker always
gets removed even if publish fails — the site never gets stuck offline.

Verify the deploy took:

```powershell
Invoke-WebRequest http://dashboard/ -UseBasicParsing | Select-Object StatusCode
# Expect: 200

# And for print-view changes specifically, browse to:
#   http://dashboard/originations/print
# The change should be visible immediately after a hard refresh (Ctrl+F5).
```

## Prerequisites (installed once, listed for reference)

Both live on DASHBOARD:

- **Microsoft Edge** at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`
  (used for headless PDF capture; ships with Windows)
- **OneDrive client** (per-machine install, installed via `/allusers`)
  signed in as `landon.watts@sunamericanmortgage.com`, with the SharePoint
  library `Accounting/dashboard_exports/originations_marketing` synced

The `landon.watts` account also needs a row in `dbo.DashboardAccess` with
`dashboard_key = 'originations'` so `/originations/print` renders instead of
showing the "no access" page.

## The reliability caveat

The scheduled task runs with `LogonType Interactive`, and OneDrive only syncs
while its account has an active Windows session. **If `landon.watts` is not
logged into DASHBOARD when Saturday noon hits**, the PDF gets written locally
but never syncs to SharePoint and no email goes out.

Two mitigations:

1. **Keep the account logged in.** After every reboot, someone RDPs in as
   `landon.watts` and locks the session (`Ctrl+Alt+Del → Lock`) so the
   OneDrive session persists.
2. **Enable auto-logon.** Configure Windows to auto-sign-in `landon.watts` at
   boot using Sysinternals `Autologon.exe` (encrypts the password into
   registry). DASHBOARD then always has a live session and OneDrive keeps
   syncing across reboots. Trade-off: reduced physical/RDP security.

Auto-logon is the industry-standard setup for headless report servers of
this shape.

## Health checks

### Was the last scheduled run successful?

```powershell
Get-ScheduledTaskInfo -TaskName "Originations Weekly PDF" | Format-List LastRunTime, LastTaskResult, NextRunTime
```

- `LastTaskResult = 0`: success
- Any other value: failure; check Task Scheduler → Task Scheduler Library
  → find "Originations Weekly PDF" → **History** tab

### Are PDFs actually landing in SharePoint?

Open the SharePoint folder in a browser and compare against the local
OneDrive folder. They should always match; if the local folder has files
that aren't in SharePoint, OneDrive sync is stuck (see Troubleshooting).

### Is Power Automate firing?

Check the flow's run history in Power Automate. Every new PDF should trigger
a successful run within ~30 seconds of the file appearing in SharePoint.

## Troubleshooting

### "LastTaskResult" isn't 0

Task Scheduler → Task Scheduler Library → find "Originations Weekly PDF"
→ **History** tab shows the exit code and any error text. Common causes:

| Symptom | Cause | Fix |
|---|---|---|
| Result 267014 ("terminated by user") | Someone right-clicked → End Task | Just retry with `Start-ScheduledTask` |
| Result 2147942405 ("access denied") | Runtime script permission issue | Verify `C:\Reports\Capture-Originations.ps1` is readable by the task account |
| Task doesn't fire at all at trigger time | `landon.watts` isn't logged in | Log in via RDP, or set up auto-logon |
| Script exits fast, `LastTaskResult = 0`, but no PDF | PS 5.1 parser error rare edge cases | Run manually to see the error output |

### The script runs manually but nothing appears in SharePoint

1. Verify the PDF is actually in the local OneDrive folder:
   ```powershell
   Get-ChildItem "C:\Users\landon.watts\OneDrive - Sun American Mortgage Company\Accounting - originations_marketing" -Filter "Weekly_Originations-*.pdf" | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime
   ```
   If not there, PDF generation itself failed — run the script manually and read
   the console output.

2. Check the OneDrive tray icon status:
   - Green cloud with checkmark: fully synced
   - Blue circling arrows: syncing (wait 30 seconds)
   - Red X: sign-in error or sync paused; click the icon to see the message

3. If sync is stuck, restart OneDrive:
   ```powershell
   Get-Process OneDrive -ErrorAction SilentlyContinue | Stop-Process -Force
   Start-Sleep -Seconds 3
   & "C:\Program Files\Microsoft OneDrive\OneDrive.exe" /background
   ```
   Give it a minute after that, then re-check SharePoint.

### The PDF looks wrong (missing content, weird layout)

1. Browse to `http://dashboard/originations/print` — does the LIVE page look
   right? If yes but the PDF doesn't, it's a print-CSS issue in
   [OriginationsPrint.razor.css](../blazor-webapp/Components/Pages/OriginationsPrint.razor.css)
   — usually the `@page` size in
   [OriginationsPrint.razor](../blazor-webapp/Components/Pages/OriginationsPrint.razor)
   needs adjusting.
2. If the LIVE page also looks wrong, it's a data or a Blazor rendering
   issue — not a print pipeline problem.

### Power Automate flow doesn't fire

- Confirm the PDF is visible in the SharePoint web view (not just the local
  OneDrive folder).
- In Power Automate, open the flow's run history — is it triggering but
  failing, or not triggering at all?
- Trigger-not-firing is usually a Flow-side connector auth issue; refresh
  the connection in Power Automate.

### Everything worked last week and today's run silently produced nothing

Almost always OneDrive got signed out or a reboot happened without
auto-logon. Verify `landon.watts` is currently logged in on DASHBOARD and
OneDrive shows "Backed up and synced" in the tray.

## Change history

- **2026-07-23** — Initial pipeline set up on branch `main`. Script writes to
  `%TEMP%` then moves to OneDrive folder. Runs Saturdays 12:00 PM as
  `SUNAMERICAN\landon.watts` (Interactive logon type).
