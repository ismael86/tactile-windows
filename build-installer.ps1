<#
.SYNOPSIS
    Builds Tactile-Setup-<version>.exe.

.DESCRIPTION
    Publishes a self-contained single-file Tactile.exe and compiles it into an
    Inno Setup installer. The version comes from <Version> in Tactile.csproj so
    it is only ever written in one place.

    Requires Inno Setup 6:  winget install -e --id JRSoftware.InnoSetup

.PARAMETER SkipPublish
    Reuse the existing publish\installer output. Handy when iterating on the
    .iss script alone, since the publish step is the slow part.
#>

[CmdletBinding()]
param(
    [switch]$SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$Csproj     = Join-Path $Root 'Tactile.csproj'
$PublishDir = Join-Path $Root 'publish\installer'
$Iss        = Join-Path $Root 'installer\Tactile.iss'
$DistDir    = Join-Path $Root 'dist'

# --- version -------------------------------------------------------------
$version = ([xml](Get-Content $Csproj)).Project.PropertyGroup.Version
if (-not $version) { throw "No <Version> found in $Csproj" }
$version = $version.Trim()
Write-Host "Tactile $version" -ForegroundColor Cyan

# --- publish -------------------------------------------------------------
if ($SkipPublish) {
    if (-not (Test-Path (Join-Path $PublishDir 'Tactile.exe'))) {
        throw "-SkipPublish was given but $PublishDir\Tactile.exe does not exist."
    }
    Write-Host "Reusing existing publish output." -ForegroundColor DarkGray
} else {
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    # Self-contained so there is no .NET runtime prerequisite to bootstrap, and
    # single-file so the install directory stays readable: just Tactile.exe next
    # to the tactile.json / layouts.json the app writes at runtime.
    # No PublishTrimmed — WinForms is not trim-safe.
    # No EnableCompressionInSingleFile — Inno's solid lzma2 does a better job on
    # the same bytes, and skipping it avoids a first-run decompression pass.
    Write-Host "Publishing..." -ForegroundColor Cyan
    dotnet publish $Csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=none `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

# --- compile installer ---------------------------------------------------
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles          'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it with:`n`n    winget install -e --id JRSoftware.InnoSetup`n"
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $DistDir "Tactile-Setup-$version.exe"
$mb = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nBuilt $setup ($mb MB)" -ForegroundColor Green
