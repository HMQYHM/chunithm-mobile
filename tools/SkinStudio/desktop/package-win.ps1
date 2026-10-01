$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Keep downloaded build tools inside the project so a locked user cache
    # cannot prevent packaging on a restricted Windows account.
    $env:ELECTRON_CACHE = Join-Path $root ".electron-cache"
    $env:ELECTRON_BUILDER_CACHE = Join-Path $root ".electron-builder-cache"
    # Build the unpacked application first; this target needs no NSIS download.
    npm run dist:win
    $source = Join-Path $root "desktop-dist\win-unpacked"
    $archive = Join-Path $root "desktop-dist\CHUNITHM-Mobile-Skin-Studio-win-x64.zip"
    if (-not (Test-Path $source)) { throw "Desktop output was not created: $source" }
    Compress-Archive -Path (Join-Path $source "*") -DestinationPath $archive -Force
    Write-Output "Created: $archive"
}
finally {
    Pop-Location
}
