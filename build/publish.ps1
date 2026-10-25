<#
.SYNOPSIS
  Builds, tests and publishes FramePilot as a self-contained win-x64 app; optionally builds the installer.
.EXAMPLE
  .\build\publish.ps1 -Installer -PresentMon C:\Downloads\PresentMon-2.3.1-x64.exe -PresentMonLicense C:\Downloads\LICENSE.txt
#>
param(
    [string]$Configuration = "Release",
    [switch]$Installer,
    [switch]$SkipTests,
    [string]$PresentMon,
    [string]$PresentMonLicense
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\FramePilot"
Set-Location $root

dotnet --version | Out-Host
dotnet restore FramePilot.slnx
dotnet build FramePilot.slnx -c $Configuration --no-restore
if (-not $SkipTests) { dotnet test FramePilot.slnx -c $Configuration --no-build --logger "console;verbosity=normal" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$common = @("-c", $Configuration, "-r", "win-x64", "--self-contained", "true",
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=None", "-o", $out)
dotnet publish src/FramePilot.App/FramePilot.App.csproj @common
dotnet publish src/FramePilot.PrivilegedHelper/FramePilot.PrivilegedHelper.csproj @common

Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $out -Force
if ($PresentMon) {
    if (-not $PresentMonLicense) { throw "PresentMon is MIT-licensed: pass -PresentMonLicense with its LICENSE.txt to redistribute it." }
    $tools = Join-Path $out "tools"
    New-Item -ItemType Directory -Force $tools | Out-Null
    Copy-Item $PresentMon (Join-Path $tools "PresentMon.exe") -Force
    Copy-Item $PresentMonLicense (Join-Path $tools "PresentMon-LICENSE.txt") -Force
}

Write-Host "Published to $out"

if ($Installer) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php" }
    & $iscc "/DSourceDir=$out" "/DOutputDir=$(Join-Path $root 'artifacts')" (Join-Path $root "installer\FramePilot.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with $LASTEXITCODE" }
}
