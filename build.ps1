<#
.SYNOPSIS
  Builds CausaFix for each Revit year, runs the automated tests and creates the installable package.

.DESCRIPTION
  Requirements (build machine only - end users need none of this):
    - Windows x64, .NET 8 SDK
    - .NET Framework 4.8 Developer Pack (for the Revit 2024 build)
    - RevitAPI.dll / RevitAPIUI.dll of each year: an installed Revit (default
      "C:\Program Files\Autodesk\Revit <year>"), or -RevitApiRoot with subfolders 2024\, 2025\, 2026\,
      or -UseRevitNuGet to use reference packages from NuGet.
  Years whose Revit API cannot be found are skipped and reported.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -Years 2026
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -RevitApiRoot D:\RevitApi
#>
[CmdletBinding()]
param(
    [string[]] $Years = @('2024', '2025', '2026'),
    [string] $RevitApiRoot = '',
    [switch] $UseRevitNuGet,
    [switch] $SkipTests,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$dist = Join-Path $root "dist\CausaFix-$version"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not $SkipTests) {
    Write-Host '== Automated tests (Revit-independent rules)' -ForegroundColor Cyan
    dotnet run --project (Join-Path $root 'tests\CausaFix.Core.Tests') -c $Configuration -f net8.0
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - package not created.' }
}

$built = @()
$skipped = @()
foreach ($year in $Years) {
    $apiDir = if ($RevitApiRoot) { Join-Path $RevitApiRoot $year } else { Join-Path $env:ProgramW6432 "Autodesk\Revit $year" }
    $buildArgs = @('build', (Join-Path $root 'src\CausaFix.Addin\CausaFix.Addin.csproj'), '-c', $Configuration, "-p:RevitVersion=$year")
    if ($UseRevitNuGet) {
        $buildArgs += '-p:UseRevitNuGet=true'
    } elseif (-not (Test-Path (Join-Path $apiDir 'RevitAPI.dll'))) {
        Write-Warning "Revit $year : RevitAPI.dll not found in '$apiDir' - skipped."
        $skipped += $year
        continue
    } else {
        $buildArgs += "-p:RevitApiDir=$apiDir"
    }
    $out = Join-Path $dist $year
    $buildArgs += @('-o', $out)
    Write-Host "== Revit $year" -ForegroundColor Cyan
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $year." }

    # Safety: never ship Autodesk assemblies.
    Get-ChildItem $out -Filter 'RevitAPI*.dll' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $out -Filter 'AdWindows.dll' -ErrorAction SilentlyContinue | Remove-Item -Force
    $built += $year
}

Copy-Item (Join-Path $root 'install\*') $dist -Force
Copy-Item (Join-Path $root 'docs\GUIA.md') $dist -Force
Copy-Item (Join-Path $root 'docs\PROJECT_SETUP.md') $dist -Force
Copy-Item (Join-Path $root 'LICENSE.txt') $dist -Force -ErrorAction SilentlyContinue

$zip = Join-Path $root "dist\CausaFix-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip

Write-Host ''
Write-Host "Built: $($built -join ', ')   Skipped: $($skipped -join ', ')" -ForegroundColor Green
Write-Host "Package: $zip"
