# uninstall.ps1 — Completely remove LightMonitor from this machine
# Safe to run even if the app was never installed.

$ErrorActionPreference = "SilentlyContinue"
$TaskName = "LightMonitor_AutoStart"
$destDir  = "$env:LOCALAPPDATA\LightMonitor"

Write-Host "Uninstalling LightMonitor..." -ForegroundColor Cyan

# ── 1. Kill running process ──────────────────────────────────────────────────
$p = Get-Process -Name "LightMonitor" -ErrorAction SilentlyContinue
if ($p) {
    Write-Host "  Stopping running instance..."
    $p | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# ── 2. Remove Scheduled Task (startup entry) ─────────────────────────────────
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Write-Host "  Removing startup task..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# ── 3. Remove installed files ────────────────────────────────────────────────
if (Test-Path $destDir) {
    Write-Host "  Deleting $destDir ..."
    Remove-Item $destDir -Recurse -Force
}

# ── 4. Remove Start Menu shortcut ────────────────────────────────────────────
$lnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\LightMonitor.lnk"
if (Test-Path $lnk) {
    Write-Host "  Removing Start Menu shortcut..."
    Remove-Item $lnk -Force
}

Write-Host ""
Write-Host "LightMonitor has been completely removed." -ForegroundColor Green
Write-Host "(Log files were in $destDir and have been deleted too.)"
