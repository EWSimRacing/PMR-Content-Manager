<# .SYNOPSIS
    Removes orphaned mod payload cache folders from EWSR_PMR_ModApp.
    
    Due to a bug where uninstall did not delete the cached mod payload, the
    mods\ folder accumulates orphaned directories from previous installs.
    This script compares folders on disk against the manifest and removes
    any that are no longer tracked.

.PARAMETER DryRun
    When $true (default), shows what would be deleted without removing anything.
    Pass -NoDryRun to actually delete the orphaned folders.

.EXAMPLE
    .\EWS_Cleanup_OrphanedPayloads.ps1
    # Preview mode - shows orphans and estimated space savings

.EXAMPLE
    .\EWS_Cleanup_OrphanedPayloads.ps1 -NoDryRun
    # Actually deletes orphaned payload folders
#>
[CmdletBinding()]
param(
    [switch]$NoDryRun
)

$DryRun = -not $NoDryRun.IsPresent

$appDataRoot  = Join-Path $env:APPDATA "EWSR_PMR_ModApp"
$modsCache    = Join-Path $appDataRoot "mods"
$manifestFile = Join-Path $appDataRoot "manifest.json"

if (-not (Test-Path $manifestFile)) {
    Write-Error "Manifest not found at: $manifestFile"
    exit 1
}

if (-not (Test-Path $modsCache)) {
    Write-Host "No mods cache folder found. Nothing to clean."
    exit 0
}

# Load manifest and get active mod IDs
$manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
$activeIds = @($manifest.Mods.PSObject.Properties.Name)

Write-Host "=== EWSR PMR ModApp - Orphaned Payload Cleanup ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Active mods in manifest: $($activeIds.Count)"

# Get all folders in mods cache
$allFolders = Get-ChildItem $modsCache -Directory
$orphans = $allFolders | Where-Object { $_.Name -notin $activeIds }

Write-Host "Total payload folders on disk: $($allFolders.Count)"
Write-Host "Orphaned (not in manifest): $($orphans.Count)"
Write-Host ""

if ($orphans.Count -eq 0) {
    Write-Host "No orphans found. Disk is clean." -ForegroundColor Green
    exit 0
}

# Calculate orphan size
$totalBytes = 0
$orphanDetails = foreach ($dir in $orphans) {
    $size = (Get-ChildItem $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    $totalBytes += $size
    [PSCustomObject]@{
        Name     = $dir.Name.Substring(0, 8) + "..."
        SizeMB   = [math]::Round($size / 1MB, 1)
        SizeGB   = [math]::Round($size / 1GB, 2)
        Modified = $dir.LastWriteTime
    }
}

Write-Host "Space to reclaim: $([math]::Round($totalBytes / 1GB, 2)) GB" -ForegroundColor Yellow
Write-Host ""

if ($DryRun) {
    Write-Host "[DRY RUN] Would delete $($orphans.Count) folders:" -ForegroundColor Yellow
    $orphanDetails | Sort-Object SizeMB -Descending | Select-Object -First 20 | Format-Table Name, SizeMB, Modified -AutoSize
    if ($orphans.Count -gt 20) {
        Write-Host "  ... and $($orphans.Count - 20) more"
    }
    Write-Host ""
    Write-Host "Run with -NoDryRun to actually delete." -ForegroundColor Cyan
}
else {
    Write-Host "Deleting $($orphans.Count) orphaned folders..." -ForegroundColor Red
    $deleted = 0
    $failed  = 0
    foreach ($dir in $orphans) {
        try {
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction Stop
            $deleted++
            if ($deleted % 25 -eq 0) {
                Write-Host "  Progress: $deleted / $($orphans.Count)" -ForegroundColor Gray
            }
        }
        catch {
            Write-Warning "Failed to delete $($dir.Name): $_"
            $failed++
        }
    }
    Write-Host ""
    Write-Host "Done. Deleted: $deleted | Failed: $failed | Reclaimed: ~$([math]::Round($totalBytes / 1GB, 2)) GB" -ForegroundColor Green
}
