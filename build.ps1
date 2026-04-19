# build.ps1 — Compile LightMonitor to a single .exe
# Run from the repo root: .\build.ps1

$ErrorActionPreference = "Stop"
$srcDir  = Join-Path $PSScriptRoot "src"
$distDir = Join-Path $PSScriptRoot "dist"

Write-Host "==> Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore "$srcDir\LightMonitor.csproj"

Write-Host "==> Publishing single-file release build..." -ForegroundColor Cyan
dotnet publish "$srcDir\LightMonitor.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$distDir"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED." -ForegroundColor Red
    exit 1
}

$exe = Get-Item "$distDir\LightMonitor.exe" -ErrorAction SilentlyContinue
if ($exe) {
    $sizeMB = [math]::Round($exe.Length / 1MB, 1)
    Write-Host ""
    Write-Host "Build complete!" -ForegroundColor Green
    Write-Host "  Output : $($exe.FullName)"
    Write-Host "  Size   : ${sizeMB} MB"
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  Install  : .\install.ps1"
    Write-Host "  Uninstall: .\uninstall.ps1"
}
