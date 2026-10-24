<#
.SYNOPSIS
  Installs CausaFix for the current user (default) or for every user of this PC.

.DESCRIPTION
  -Scope User     (default, no administrator rights)
      binaries  -> %AppData%\CausaFix\bin\<year>\
      manifest  -> %AppData%\Autodesk\Revit\Addins\<year>\CausaFix.addin
  -Scope Machine  (administrator rights; the script asks for elevation)
      binaries  -> %ProgramFiles%\CausaFix\<year>\
      manifest  -> %ProgramData%\Autodesk\Revit\Addins\<year>\CausaFix.addin

  Only CausaFix files are written. Other add-ins and their manifests are never touched.
  Re-running updates the installation in place. Installing in one scope removes the current user's
  CausaFix manifest of the other scope, so Revit never loads CausaFix twice.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Install.ps1
  powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Scope Machine
  powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Years 2026
#>
[CmdletBinding()]
param(
    [ValidateSet('User', 'Machine')]
    [string] $Scope = 'User',
    [string[]] $Years = @('2024', '2025', '2026'),
    [string] $PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Scope -eq 'Machine' -and -not (Test-Admin)) {
    Write-Host 'Installing for all users needs administrator rights. Requesting elevation...' -ForegroundColor Yellow
    $argList = @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Scope', 'Machine',
                 '-Years', ($Years -join ','), '-PackageRoot', "`"$PackageRoot`"")
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
    exit $p.ExitCode
}

# Accept "-Years 2025,2026" passed as one string after elevation.
$Years = @($Years | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

$userAppData = [Environment]::GetFolderPath('ApplicationData')
$programData = [Environment]::GetFolderPath('CommonApplicationData')
$programFiles = [Environment]::GetFolderPath('ProgramFiles')

function Get-Paths([string] $scope, [string] $year) {
    if ($scope -eq 'Machine') {
        return @{ Bin = (Join-Path $programFiles "CausaFix\$year"); Addins = (Join-Path $programData "Autodesk\Revit\Addins\$year") }
    }
    return @{ Bin = (Join-Path $userAppData "CausaFix\bin\$year"); Addins = (Join-Path $userAppData "Autodesk\Revit\Addins\$year") }
}

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Warning 'Revit is running. Close Revit before installing or updating CausaFix (its files are locked while Revit runs).'
    exit 1
}

$template = Get-Content -Raw -Path (Join-Path $PackageRoot 'CausaFix.addin.template')
$other = if ($Scope -eq 'Machine') { 'User' } else { 'Machine' }
$installed = @()

foreach ($year in $Years) {
    $source = Join-Path $PackageRoot $year
    if (-not (Test-Path (Join-Path $source 'CausaFix.Addin.dll'))) {
        Write-Host "Revit $year : no binaries in this package - skipped." -ForegroundColor DarkGray
        continue
    }
    $paths = Get-Paths $Scope $year
    New-Item -ItemType Directory -Force -Path $paths.Bin | Out-Null
    New-Item -ItemType Directory -Force -Path $paths.Addins | Out-Null

    # Replace the previous CausaFix binaries of this year only.
    Get-ChildItem -Path $paths.Bin -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item -Path (Join-Path $source '*') -Destination $paths.Bin -Recurse -Force

    $assembly = Join-Path $paths.Bin 'CausaFix.Addin.dll'
    $manifest = $template.Replace('{ASSEMBLY_PATH}', [System.Security.SecurityElement]::Escape($assembly))
    [System.IO.File]::WriteAllText((Join-Path $paths.Addins 'CausaFix.addin'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

    # Never register CausaFix twice: remove this user's manifest of the other scope.
    $otherPaths = Get-Paths $other $year
    $otherManifest = Join-Path $otherPaths.Addins 'CausaFix.addin'
    if (Test-Path $otherManifest) {
        try {
            Remove-Item -Force $otherManifest
            Write-Host "Revit $year : removed the $other installation manifest to avoid loading CausaFix twice." -ForegroundColor Yellow
        } catch {
            Write-Warning "Revit $year : a $other installation also exists ($otherManifest). Uninstall it to avoid loading CausaFix twice."
        }
    }

    # Warn about any other manifest that loads CausaFix (would register it twice).
    Get-ChildItem -Path $paths.Addins -Filter '*.addin' | Where-Object { $_.Name -ne 'CausaFix.addin' } | ForEach-Object {
        if ((Get-Content -Raw $_.FullName) -match 'CausaFix\.Addin\.dll') {
            Write-Warning "Another manifest also loads CausaFix: $($_.FullName). Remove it to avoid a duplicate registration."
        }
    }

    Write-Host "Revit $year : installed for $(if ($Scope -eq 'Machine') { 'all users' } else { 'the current user' }) -> $($paths.Bin)" -ForegroundColor Green
    $installed += $year
}

if ($installed.Count -eq 0) {
    Write-Warning 'Nothing was installed: this package contains no binaries for the requested years.'
    exit 2
}
Write-Host ''
Write-Host "CausaFix installed for Revit $($installed -join ', '). Start Revit and open the 'CausaFix' tab." -ForegroundColor Cyan
if ($Scope -eq 'Machine') {
    Write-Host 'Note: users who also installed CausaFix for themselves should run Uninstall.cmd once, so Revit does not load it twice.' -ForegroundColor DarkYellow
}
