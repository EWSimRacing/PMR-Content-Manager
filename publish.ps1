# publish.ps1 - PMR CM portable single-file publish script
# Usage:
#   .\publish.ps1              # Dry-run: shows what would be built
#   .\publish.ps1 -DryRun:$false   # Actually builds to .\dist\
#
# Output: dist\PMR CM.exe + dist\PMR CM.Helper.exe (self-contained, no runtime required)
# WARNING: Do NOT add PublishTrimmed=true. WPF uses reflection; trimming breaks bindings.

param(
    [bool]$DryRun = $true
)

$ErrorActionPreference = "Stop"

$RepoRoot       = $PSScriptRoot
$UIProject      = Join-Path $RepoRoot "src\EWSR_PMR_ModApp.UI\EWSR_PMR_ModApp.UI.csproj"
$HelperProject  = Join-Path $RepoRoot "src\EWSR_PMR_ModApp.Helper\EWSR_PMR_ModApp.Helper.csproj"
$DistDir        = Join-Path $RepoRoot "dist"

# UI publish args (WPF -- no trimming)
$UIArgs = @(
    "publish", $UIProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:ReadyToRun=true",
    "-o", $DistDir
)

# Helper publish args (console exe -- no WPF, trimming also unsafe due to JSON reflection)
$HelperArgs = @(
    "publish", $HelperProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:ReadyToRun=true",
    "-o", $DistDir
)

Write-Host ""
Write-Host "PMR CM -- Publish Script" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Mode       : $(if ($DryRun) { 'DRY RUN (no changes)' } else { 'LIVE -- will write to dist\' })"
Write-Host "  UI project : $UIProject"
Write-Host "  Helper     : $HelperProject"
Write-Host "  Target     : win-x64, self-contained, single-file"
Write-Host "  Output dir : $DistDir"
Write-Host ""

if ($DryRun) {
    Write-Host "[DRY RUN] No files were written. Run with -DryRun:`$false to publish." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# Clean the dist directory before publishing.
if (Test-Path $DistDir) {
    Write-Host "Cleaning existing dist\ ..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $DistDir
}

Write-Host "Publishing PMR CM.exe ..." -ForegroundColor Green
& dotnet @UIArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "UI publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Publishing PMR CM.Helper.exe ..." -ForegroundColor Green
& dotnet @HelperArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Helper publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Publish succeeded. Output:" -ForegroundColor Green
Get-ChildItem $DistDir -File | Sort-Object Name | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 2)
    Write-Host ("  {0,-40} {1,8} MB" -f $_.Name, $sizeMB)
}
Write-Host ""
Write-Host "Single-file check (should be 2 exes, no loose DLLs):" -ForegroundColor Cyan
$exeCount = (Get-ChildItem $DistDir -Filter "*.exe").Count
$dllCount = (Get-ChildItem $DistDir -Filter "*.dll").Count
Write-Host "  .exe files : $exeCount"
Write-Host "  .dll files : $dllCount"
if ($dllCount -gt 0) {
    Write-Warning "Loose DLLs found -- single-file bundling may not have worked as expected."
} else {
    Write-Host "  No loose DLLs -- clean single-file build." -ForegroundColor Green
}
Write-Host ""
