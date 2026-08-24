# Builds SkyrimVersionManager as a single standalone exe (no installation required).
# Output: .\publish\SkyrimVersionManager.exe
$ErrorActionPreference = "Stop"

# Prefer a dotnet that actually has an SDK (a runtime-only dotnet on PATH can't build).
$dotnet = $null
$candidates = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $candidates += (Get-Command dotnet).Source }
$candidates += (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe")
foreach ($c in $candidates) {
    if ((Test-Path $c) -and (& $c --list-sdks 2>$null)) { $dotnet = $c; break }
}
if (-not $dotnet) { throw ".NET SDK not found. Install from https://aka.ms/dotnet/download" }

& $dotnet publish "$PSScriptRoot\SkyrimVersionManager.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$PSScriptRoot\publish"

Write-Host ""
Write-Host "Built: $PSScriptRoot\publish\SkyrimVersionManager.exe"

# ---- Package a clean release zip -----------------------------------------------------------
# Only the exe + docs go in. The publish\data folder (personal settings, cached game depots,
# stashed saves) must NEVER be distributed: it contains the user's own data and Bethesda's
# copyrighted game files. The whitelist check below makes shipping it impossible by accident.
$csproj = Get-Content "$PSScriptRoot\SkyrimVersionManager.csproj" -Raw
$version = "0.0.0"
if ($csproj -match '<Version>([^<]+)</Version>') { $version = $Matches[1] }

$releaseDir = Join-Path $PSScriptRoot "release"
New-Item -ItemType Directory -Force $releaseDir | Out-Null
$stage = Join-Path $releaseDir "stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item "$PSScriptRoot\publish\SkyrimVersionManager.exe" $stage
Copy-Item "$PSScriptRoot\README.md" $stage
Copy-Item "$PSScriptRoot\LICENSE" $stage

$zip = Join-Path $releaseDir "SkyrimVersionManager-v$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item -Recurse -Force $stage

# Safety check: fail the build if anything beyond the whitelist slipped into the zip.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
$entries = @($archive.Entries | ForEach-Object { $_.FullName })
$archive.Dispose()
$allowed = @("SkyrimVersionManager.exe", "README.md", "LICENSE")
$unexpected = @($entries | Where-Object { $allowed -notcontains $_ })
if ($unexpected.Count -gt 0) {
    Remove-Item -Force $zip
    throw "Release zip contained unexpected files and was deleted: $($unexpected -join ', ')"
}

Write-Host "Release zip: $zip"
Write-Host "  Contents: $($entries -join ', ')"
if (Test-Path "$PSScriptRoot\publish\data") {
    Write-Host "  Note: publish\data (your personal working data) was NOT included - never upload it."
}
