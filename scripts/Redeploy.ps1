# -----------------------------------------------------------------------------
# Redeploy.ps1
#
# Pulls the latest main from GitHub, brings the Blazor app offline gracefully
# via app_offline.htm so IIS releases the DLL locks, republishes, and brings
# the site back up. Runs cleanly whether or not IIS is actively serving
# traffic when it kicks off.
#
# Also refreshes the runtime copy of Capture-Originations.ps1 so any changes
# to the capture script land in C:\Reports\ in the same operation.
#
# Prerequisites (installed once on DASHBOARD):
#   * .NET 8 SDK (dotnet.exe on PATH)
#   * Git for Windows (git.exe on PATH)
#   * Write access to $PublishPath and $CaptureRuntimePath below (elevated
#     PowerShell handles this by default)
#
# Usage - normal redeploy after a git commit lands on main:
#   powershell -ExecutionPolicy Bypass -File .\Redeploy.ps1
#
# Usage - skip git pull (publish code already in place):
#   powershell -ExecutionPolicy Bypass -File .\Redeploy.ps1 -SkipPull
#
# Usage - skip the capture-script refresh:
#   powershell -ExecutionPolicy Bypass -File .\Redeploy.ps1 -SkipCaptureScript
# -----------------------------------------------------------------------------

[CmdletBinding()]
param(
    [string] $RepoRoot            = "C:\inetpub\wwwroot\blazor",
    [string] $ProjectDir          = "C:\inetpub\wwwroot\blazor\blazor-webapp",
    [string] $PublishPath         = "C:\inetpub\wwwroot\blazor\blazor-webapp\bin\Release\net8.0\publish",
    [string] $CaptureSourcePath   = "C:\inetpub\wwwroot\blazor\scripts\Capture-Originations.ps1",
    [string] $CaptureRuntimePath  = "C:\Reports\Capture-Originations.ps1",
    [int]    $OfflineWaitSeconds  = 3,
    [switch] $SkipPull,
    [switch] $SkipCaptureScript
)

$ErrorActionPreference = "Stop"
$offlineFile = Join-Path $PublishPath "app_offline.htm"

function Write-Step([string] $msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

# --- 1. Sanity checks --------------------------------------------------------
Write-Step "Sanity checks"

if (-not (Test-Path $RepoRoot))   { throw "Repo root not found: $RepoRoot" }
if (-not (Test-Path $ProjectDir)) { throw "Project directory not found: $ProjectDir" }

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git.exe not on PATH. Install Git for Windows or add it to PATH."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet.exe not on PATH. Install the .NET 8 SDK or add it to PATH."
}
Write-Host "OK: git, dotnet, and target directories are present."

# --- 2. Pull latest main -----------------------------------------------------
if (-not $SkipPull) {
    Write-Step "Pulling latest main"
    Push-Location $RepoRoot
    try {
        & git pull origin main
        if ($LASTEXITCODE -ne 0) {
            throw "git pull failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Step "Skipping git pull (-SkipPull specified)"
}

# --- 3. Bring the app offline gracefully -------------------------------------
Write-Step "Taking site offline"

if (-not (Test-Path $PublishPath)) {
    New-Item -ItemType Directory -Path $PublishPath -Force | Out-Null
}

# app_offline.htm is a magic filename the ASP.NET Core IIS module watches for.
# The moment it appears, the worker process flushes pending requests and
# exits, releasing the file locks that would otherwise block publish.
New-Item -ItemType File -Path $offlineFile -Force | Out-Null
Write-Host "Marker placed: $offlineFile"
Start-Sleep -Seconds $OfflineWaitSeconds

# --- 4. Publish --------------------------------------------------------------
# Everything after this point runs inside try/finally so the offline marker
# always gets cleaned up even if publish blows up. Otherwise a failed publish
# would leave the site permanently offline.
try {
    Write-Step "Publishing"
    Push-Location $ProjectDir
    try {
        & dotnet publish -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }

    # --- 5. Refresh the runtime capture script -------------------------------
    if (-not $SkipCaptureScript) {
        Write-Step "Refreshing capture script"
        if (Test-Path $CaptureSourcePath) {
            $runtimeDir = Split-Path $CaptureRuntimePath -Parent
            if (-not (Test-Path $runtimeDir)) {
                New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
            }
            Copy-Item -Path $CaptureSourcePath -Destination $CaptureRuntimePath -Force
            Write-Host "Copied: $CaptureSourcePath -> $CaptureRuntimePath"
        } else {
            Write-Warning "Capture source not found at $CaptureSourcePath; skipping runtime refresh."
        }
    } else {
        Write-Step "Skipping capture script refresh (-SkipCaptureScript specified)"
    }
} finally {
    # --- 6. Always bring the site back online --------------------------------
    Write-Step "Bringing site back online"
    if (Test-Path $offlineFile) {
        Remove-Item $offlineFile -Force
        Write-Host "Marker removed: site will respond to next request."
    }
}

Write-Host ""
Write-Host "Done. Verify with:" -ForegroundColor Green
Write-Host "  Invoke-WebRequest http://dashboard/ -UseBasicParsing | Select-Object StatusCode" -ForegroundColor Green
