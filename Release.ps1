# Creates a portable release; GitHub publication is explicitly opt-in.
[CmdletBinding()]
param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",
    [switch]$PublishToGitHub
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Repository = "EWSimRacing/PMR-Content-Manager"
$UIProject = Join-Path $RepoRoot "src\EWSR_PMR_ModApp.UI\EWSR_PMR_ModApp.UI.csproj"
$HelperProject = Join-Path $RepoRoot "src\EWSR_PMR_ModApp.Helper\EWSR_PMR_ModApp.Helper.csproj"
$ReadmePath = Join-Path $RepoRoot "README.md"
$ChangelogPath = Join-Path $RepoRoot "CHANGELOG.md"
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Invoke-Checked {
    param([string]$Command, [string[]]$CommandArguments)
    & $Command @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

Push-Location $RepoRoot
try {
    $projectText = [IO.File]::ReadAllText($UIProject)
    $versionMatch = [regex]::Match($projectText, '<Version>(\d+\.\d+\.\d+)</Version>')
    if (-not $versionMatch.Success) { throw "UI project version was not found." }
    $current = [version]$versionMatch.Groups[1].Value
    $next = switch ($Bump) {
        "major" { "{0}.0.0" -f ($current.Major + 1) }
        "minor" { "{0}.{1}.0" -f $current.Major, ($current.Minor + 1) }
        "patch" { "{0}.{1}.{2}" -f $current.Major, $current.Minor, ($current.Build + 1) }
    }
    $tag = "v$next"
    $changelog = [IO.File]::ReadAllText($ChangelogPath)
    $notesMatch = [regex]::Match($changelog,
        "(?ms)^## $([regex]::Escape($tag)) [^\r\n]*\r?\n(?<notes>.*?)(?=^## |\z)")
    if (-not $notesMatch.Success) {
        throw "Add a CHANGELOG.md section for $tag before releasing."
    }

    $publishDir = Join-Path $RepoRoot "artifacts\publish\$tag"
    $packageDir = Join-Path $RepoRoot "artifacts\packages\$tag"
    $zipPath = Join-Path $RepoRoot "artifacts\PMR-Content-Manager-$tag-win-x64.zip"
    foreach ($path in @($publishDir, $packageDir, $zipPath)) {
        if (Test-Path -LiteralPath $path) { throw "Release output already exists: $path" }
    }

    if ($PublishToGitHub) {
        Invoke-Checked "gh" @("auth", "status")
        $dirty = & git status --porcelain -- src tests docs README.md CHANGELOG.md Release.ps1 publish.ps1
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect release source." }
        if ($dirty) { throw "Commit release source changes before using -PublishToGitHub." }
        $branch = & git branch --show-current
        if ($LASTEXITCODE -ne 0 -or $branch -ne "master") {
            throw "Publish releases from master."
        }
        Invoke-Checked "git" @("fetch", "origin", "master", "--tags")
        $head = & git rev-parse HEAD
        $remote = & git rev-parse origin/master
        if ($LASTEXITCODE -ne 0 -or $head -ne $remote) {
            throw "Push the release source commit to origin/master before publishing."
        }
        $existingTag = & git tag --list $tag
        if ($LASTEXITCODE -ne 0 -or $existingTag) { throw "Tag $tag already exists or could not be checked." }
    }

    Invoke-Checked "dotnet" @("test", "EWSR_PMR_ModApp.slnx", "-c", "Release",
        "--no-restore", "--nologo", "-v", "quiet")

    foreach ($project in @($UIProject, $HelperProject)) {
        $text = [IO.File]::ReadAllText($project)
        foreach ($property in @("Version", "AssemblyVersion", "FileVersion")) {
            $value = if ($property -eq "Version") { $next } else { "$next.0" }
            $pattern = "<$property>[^<]+</$property>"
            if (-not [regex]::IsMatch($text, $pattern)) { throw "Missing $property in $project" }
            $text = [regex]::Replace($text, $pattern, "<$property>$value</$property>")
        }
        [IO.File]::WriteAllText($project, $text, $Utf8)
    }
    $readme = [IO.File]::ReadAllText($ReadmePath)
    $readme = [regex]::Replace($readme, 'v\d+\.\d+\.\d+', $tag)
    [IO.File]::WriteAllText($ReadmePath, $readme, $Utf8)

    & (Join-Path $RepoRoot "publish.ps1") -DryRun:$false -OutputDirectory $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
    foreach ($name in @("PMR CM.exe", "PMR CM.Helper.exe")) {
        $path = Join-Path $publishDir $name
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing release executable: $name" }
        $actualVersion = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
        if ($actualVersion -ne "$next.0") { throw "$name has incorrect version: $actualVersion" }
    }
    if (Get-ChildItem -LiteralPath $publishDir -Filter "*.dll" -File) {
        throw "Portable publish contains loose DLLs; fix bundling before packaging."
    }

    New-Item -ItemType Directory -Path $packageDir | Out-Null
    foreach ($name in @("PMR CM.exe", "PMR CM.Helper.exe")) {
        Copy-Item -LiteralPath (Join-Path $publishDir $name) -Destination $packageDir
    }
    foreach ($name in @("README.md", "CHANGELOG.md", "LICENSE")) {
        Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination $packageDir
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath)
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -ne 5) { throw "Release ZIP must contain two executables and three documents." }
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace("-", "")
                $sourceHash = (Get-FileHash -LiteralPath (Join-Path $packageDir $entry.FullName)).Hash
                if ($archiveHash -ne $sourceHash) { throw "ZIP content verification failed: $($entry.FullName)" }
            }
            finally { $stream.Dispose(); $sha.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $checksum = (Get-FileHash -LiteralPath $zipPath).Hash
    [IO.File]::WriteAllText("$zipPath.sha256", "$checksum  $([IO.Path]::GetFileName($zipPath))`n", $Utf8)
    $notesPath = Join-Path $RepoRoot "artifacts\$tag-release-notes.md"
    [IO.File]::WriteAllText($notesPath, $notesMatch.Groups["notes"].Value.Trim(), $Utf8)

    if ($PublishToGitHub) {
        Invoke-Checked "git" @("add", "--", $UIProject, $HelperProject, $ReadmePath)
        Invoke-Checked "git" @("commit", "-m", "chore: release $tag", "-m",
            "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>")
        Invoke-Checked "git" @("tag", "-a", $tag, "-m", "PMR Content Manager $tag")
        Invoke-Checked "git" @("push", "--atomic", "origin", "master", $tag)
        Invoke-Checked "gh" @("release", "create", $tag, $zipPath, "$zipPath.sha256",
            "--repo", $Repository, "--verify-tag", "--title", "PMR Content Manager $tag",
            "--notes-file", $notesPath)
    }
    Write-Host "Release package: $zipPath"
    Write-Host "SHA-256: $checksum"
}
finally {
    Pop-Location
}
