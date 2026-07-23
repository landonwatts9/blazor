# -----------------------------------------------------------------------------
# Capture-Originations.ps1
#
# Renders /originations/print via headless Microsoft Edge, saves the resulting
# PDF locally, and uploads it to a SharePoint document library. A Power
# Automate flow watching that library sends the report via email once the
# file lands.
#
# Runs the report as of "now" by default: the print view auto-selects the
# current year, month, and Saturday-ending week when no filters are supplied.
# Pass -Year / -Month / -WeekEndingOverride to pin a specific reporting
# period (useful for regenerating a prior week).
#
# Filenames use the same UTC ISO convention as existing files in the target
# folder (Weekly_Originations-yyyy-MM-ddTHH_mm_ssZ.pdf) so the Power Automate
# flow's naming rules keep working.
#
# Prerequisites on the server:
#   * The account running this script must have a row in
#     dbo.DashboardAccess for the 'originations' key.
#   * Microsoft Edge must be installed at the path in $EdgeExe below.
#   * PnP.PowerShell module must be installed for SharePoint upload:
#         Install-Module PnP.PowerShell -Scope AllUsers -Force
#   * For scheduled (headless) runs, an Azure AD app registration with
#     Sites.Selected or Sites.ReadWrite.All permission on the target site,
#     plus a certificate installed on the server. See -AuthMode below.
#
# Usage - interactive test (opens a browser for SharePoint sign-in):
#   powershell -ExecutionPolicy Bypass -File .\Capture-Originations.ps1
#
# Usage - scheduled run (headless with certificate auth):
#   powershell -ExecutionPolicy Bypass -File .\Capture-Originations.ps1 `
#       -AuthMode Certificate `
#       -ClientId  "<app-registration-client-id>" `
#       -TenantId  "sunamericanmortgage.onmicrosoft.com" `
#       -CertThumbprint "<cert-thumbprint-from-cert-store>"
#
# Usage - local only, skip SharePoint upload entirely:
#   powershell -ExecutionPolicy Bypass -File .\Capture-Originations.ps1 -AuthMode None
# -----------------------------------------------------------------------------

[CmdletBinding()]
param(
    # --- PDF generation ------------------------------------------------------
    [string] $Url                = "http://dashboard/originations/print",
    # Default output path is the OneDrive-synced SharePoint folder for the
    # Accounting site. The PDF written here syncs up to SharePoint
    # automatically, and Power Automate fires on the resulting new-file
    # event to email the report.
    [string] $LocalDir           = "C:\Users\landon.watts\OneDrive - Sun American Mortgage Company\Accounting - originations_marketing",
    [int]    $WaitMs             = 10000,
    [string] $EdgeExe            = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    [Nullable[int]]    $Year     = $null,
    [Nullable[int]]    $Month    = $null,
    [string] $WeekEndingOverride = $null,

    # --- SharePoint upload target --------------------------------------------
    # Only used when -AuthMode is Interactive or Certificate. With
    # AuthMode=None (the default in this configuration), the PDF is written
    # to $LocalDir above and OneDrive handles the SharePoint sync.
    [string] $SharePointSiteUrl  = "https://sunamericanmortgage.sharepoint.com/sites/Accounting",
    [string] $SharePointFolder   = "Shared Documents/dashboard_exports/originations_marketing",

    # --- Auth mode -----------------------------------------------------------
    # None:         skip PnP upload entirely; PDF is left in $LocalDir and
    #               OneDrive syncs it to SharePoint. This is the default and
    #               matches the OneDrive-based deployment.
    # Interactive:  opens a browser for OAuth. For manual PnP testing only;
    #               will fail inside a scheduled task with no user session.
    # Certificate:  fully headless PnP upload; requires -ClientId, -TenantId,
    #               and either -CertThumbprint (cert already installed to
    #               CurrentUser or LocalMachine cert store) or -CertPath
    #               (PFX file on disk).
    [ValidateSet("Interactive", "Certificate", "None")]
    [string] $AuthMode           = "None",
    [string] $ClientId           = "",
    [string] $TenantId           = "",
    [string] $CertThumbprint     = "",
    [string] $CertPath           = "",
    [string] $CertPassword       = "",

    # If set, the local PDF copy is preserved after upload. Off by default so
    # the local folder does not grow forever.
    [switch] $KeepLocalCopy
)

$ErrorActionPreference = "Stop"

# --- Build the URL (optional filter overrides) -------------------------------
$query = @()
if ($Year)                { $query += "year=$Year" }
if ($Month)               { $query += "month=$Month" }
if ($WeekEndingOverride)  { $query += "weekEnding=$WeekEndingOverride" }
if ($query.Count -gt 0) {
    $Url = $Url + "?" + ($query -join "&")
}

# --- Generate the PDF --------------------------------------------------------
# UTC ISO timestamp with underscores in place of colons (colons are not
# valid in Windows filenames). Matches the existing "Weekly_Originations-"
# naming convention already used in the target SharePoint folder.
$utcStamp   = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH_mm_ss") + "Z"
$fileName   = "Weekly_Originations-$utcStamp.pdf"
$pdfPath    = Join-Path $LocalDir $fileName
New-Item -ItemType Directory -Path $LocalDir -Force | Out-Null

if (-not (Test-Path $EdgeExe)) {
    throw "Microsoft Edge not found at '$EdgeExe'. Pass -EdgeExe with the correct path."
}

Write-Host "Capturing: $Url"
Write-Host "Final out: $pdfPath"

# Edge writes to a temp path first, then we move the finished PDF to
# $pdfPath. Two reasons:
#   1. Start-Process -ArgumentList does not quote arguments that contain
#      spaces, so passing --print-to-pdf=<path with spaces> results in
#      Edge only seeing the first token of the path.
#   2. OneDrive-synced folders sometimes reject in-place writes while
#      still finalising a previous sync operation; writing to a plain
#      local path avoids that whole class of races.
$tempDir = Join-Path $env:TEMP "SamReporting"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}
$tempPdfPath = Join-Path $tempDir $fileName

# Edge headless on Windows Server spawns helper processes and the invoking
# msedge.exe can exit before the PDF is fully written. Start-Process -Wait
# blocks on the process tree, and the polling loop afterwards is a safety
# net for the rare case where the file still hasn't flushed to disk.
$edgeArgs = @(
    '--headless=new',
    '--disable-gpu',
    '--hide-scrollbars',
    '--no-sandbox',
    '--auth-server-allowlist=dashboard',
    "--virtual-time-budget=$WaitMs",
    '--no-pdf-header-footer',
    "--print-to-pdf=$tempPdfPath",
    $Url
)
Start-Process -FilePath $EdgeExe -ArgumentList $edgeArgs -Wait -NoNewWindow -ErrorAction Stop

$deadline = (Get-Date).AddSeconds(15)
while (-not (Test-Path $tempPdfPath -PathType Leaf) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path $tempPdfPath -PathType Leaf)) {
    throw "PDF was not created at $tempPdfPath. Check that Edge is installed, /originations/print is reachable, and the run account has 'originations' access."
}

Move-Item -Path $tempPdfPath -Destination $pdfPath -Force
$sizeKb = [math]::Round((Get-Item $pdfPath).Length / 1KB, 1)
Write-Host "PDF ready ($sizeKb KB)."

# --- SharePoint upload -------------------------------------------------------
if ($AuthMode -eq "None") {
    Write-Host "AuthMode=None. Skipping SharePoint upload; PDF stays at $pdfPath."
    exit 0
}

if (-not (Get-Module -ListAvailable -Name PnP.PowerShell)) {
    throw "PnP.PowerShell module not installed. Run: Install-Module PnP.PowerShell -Scope AllUsers -Force"
}
Import-Module PnP.PowerShell

try {
    Write-Host ("Connecting to {0} using {1} auth..." -f $SharePointSiteUrl, $AuthMode)
    switch ($AuthMode) {
        "Interactive" {
            # Opens a browser; caches a token for repeat runs in the same
            # user session. Not viable for a scheduled task with no UI.
            Connect-PnPOnline -Url $SharePointSiteUrl -Interactive -ErrorAction Stop
        }
        "Certificate" {
            if (-not $ClientId -or -not $TenantId) {
                throw "Certificate auth requires -ClientId and -TenantId."
            }
            if ($CertThumbprint) {
                Connect-PnPOnline -Url $SharePointSiteUrl `
                    -ClientId $ClientId -Tenant $TenantId `
                    -Thumbprint $CertThumbprint -ErrorAction Stop
            } elseif ($CertPath) {
                $secure = if ($CertPassword) { ConvertTo-SecureString $CertPassword -AsPlainText -Force } else { $null }
                Connect-PnPOnline -Url $SharePointSiteUrl `
                    -ClientId $ClientId -Tenant $TenantId `
                    -CertificatePath $CertPath -CertificatePassword $secure -ErrorAction Stop
            } else {
                throw "Certificate auth requires either -CertThumbprint or -CertPath."
            }
        }
    }

    Write-Host "Uploading to $SharePointFolder..."
    Add-PnPFile -Path $pdfPath -Folder $SharePointFolder -ErrorAction Stop | Out-Null
    Write-Host "OK: uploaded $fileName"
} finally {
    try { Disconnect-PnPOnline -ErrorAction SilentlyContinue } catch {}
}

# --- Cleanup -----------------------------------------------------------------
if (-not $KeepLocalCopy) {
    Remove-Item $pdfPath -Force
    Write-Host "Removed local copy."
}
