# install.ps1 — Install LightMonitor to %LOCALAPPDATA%\LightMonitor\
# Run AFTER build.ps1.  Must be run as Administrator (same as the app itself needs).

$ErrorActionPreference = "Stop"

$src     = Join-Path $PSScriptRoot "dist\LightMonitor.exe"
$destDir = "$env:LOCALAPPDATA\LightMonitor"
$dest    = "$destDir\LightMonitor.exe"

# ── Sanity check ────────────────────────────────────────────────────────────
if (-not (Test-Path $src)) {
    Write-Host "ERROR: dist\LightMonitor.exe not found. Run .\build.ps1 first." -ForegroundColor Red
    exit 1
}

# ── Stop any running instance ────────────────────────────────────────────────
$running = Get-Process -Name "LightMonitor" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running instance..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# ── Copy executable ──────────────────────────────────────────────────────────
Write-Host "Installing to $destDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $destDir -Force | Out-Null
Copy-Item $src $dest -Force

# ── Create Start Menu shortcut ───────────────────────────────────────────────
$shortcutDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
$lnk = Join-Path $shortcutDir "LightMonitor.lnk"
$ws  = New-Object -ComObject WScript.Shell
$sc  = $ws.CreateShortcut($lnk)
$sc.TargetPath       = $dest
$sc.WorkingDirectory = $destDir
$sc.Description      = "Barebones system-tray temperature monitor"
$sc.Save()

Write-Host ""
Write-Host "LightMonitor installed successfully!" -ForegroundColor Green
Write-Host "  Location : $dest"
Write-Host "  Shortcut : $lnk"
Write-Host ""
Write-Host "Launch now? (Y/N)" -ForegroundColor Yellow -NoNewline
$ans = Read-Host " "
if ($ans -match "^[Yy]") {
    Start-Process $dest
    Write-Host "Started. Look for the badge icon in your system tray." -ForegroundColor Green
}
