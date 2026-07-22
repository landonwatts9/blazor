# -----------------------------------------------------------------------------
# Capture-Originations.ps1
#
# Runs headless Microsoft Edge against the /originations/print view of the
# dashboard, saves a single-page PDF, and drops it in $OutDir with a
# timestamped filename.
#
# Runs the report as of "now" by default — the print view auto-selects the
# current year, month, and Saturday-ending week when no filters are supplied.
# Pass -WeekEndingOverride 'yyyy-MM-dd' to pin an arbitrary week (useful for
# regenerating a prior period), or use the -Year / -Month switches for a
# different reporting month.
#
# Prerequisites on the server:
#   * The account running this script must have a row in
#     dbo.DashboardAccess for the 'originations' key.
#   * Microsoft Edge must be installed at the path in $EdgeExe below;
#     override with -EdgeExe if it lives elsewhere.
#   * Redeploy the Blazor app before first use so /originations/print is live.
#
# Usage (interactive test):
#   powershell -ExecutionPolicy Bypass -File .\Capture-Originations.ps1
#
# Usage (Task Scheduler action):
#   Program:   powershell.exe
#   Arguments: -ExecutionPolicy Bypass -File "C:\Reports\Capture-Originations.ps1"
# -----------------------------------------------------------------------------

[CmdletBinding()]
param(
    [string] $Url                = "http://dashboard/originations/print",
    [string] $OutDir             = "C:\Reports\Originations",
    [int]    $WaitMs             = 10000,
    [string] $EdgeExe            = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    [Nullable[int]]    $Year     = $null,
    [Nullable[int]]    $Month    = $null,
    [string] $WeekEndingOverride = $null
)

$ErrorActionPreference = "Stop"

# Build the URL with any explicit overrides. Empty overrides let the
# print page pick "current" values on its own.
$query = @()
if ($Year)                { $query += "year=$Year" }
if ($Month)               { $query += "month=$Month" }
if ($WeekEndingOverride)  { $query += "weekEnding=$WeekEndingOverride" }
if ($query.Count -gt 0) {
    $Url = $Url + "?" + ($query -join "&")
}

# Timestamped filename encodes the moment of capture, not the report period,
# so re-running the same job at different times never overwrites a prior PDF.
$timestamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$pdfPath   = Join-Path $OutDir "Originations_$timestamp.pdf"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

if (-not (Test-Path $EdgeExe)) {
    throw "Microsoft Edge not found at '$EdgeExe'. Pass -EdgeExe with the correct path."
}

Write-Host "Capturing: $Url"
Write-Host "Saving to: $pdfPath"

# --auth-server-allowlist tells Edge to silently send NTLM credentials
# to the dashboard host so the run account signs in without a prompt.
# --virtual-time-budget gives Blazor's SignalR circuit + the async data
# fetch enough time to complete before Edge takes the print snapshot.
& $EdgeExe `
    --headless=new `
    --disable-gpu `
    --hide-scrollbars `
    --no-sandbox `
    --auth-server-allowlist="dashboard" `
    --virtual-time-budget=$WaitMs `
    --no-pdf-header-footer `
    --print-to-pdf="$pdfPath" `
    $Url

if (Test-Path $pdfPath -PathType Leaf) {
    $sizeKb = [math]::Round((Get-Item $pdfPath).Length / 1KB, 1)
    Write-Host "OK: $pdfPath ($sizeKb KB)"
} else {
    throw "PDF was not created. Check that Edge is installed, the URL is reachable, and the account has 'originations' access."
}
