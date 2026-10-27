<#
.SYNOPSIS
  Removes CausaFix for the current user (default) or for all users (-Scope Machine, needs administrator).
  Only CausaFixIt files are removed. Profiles, settings, reports and logs of the current user are kept
  unless -RemoveUserData is given.
#>
[CmdletBinding()]
param(
    [ValidateSet('User', 'Machine')]
    [string] $Scope = 'User',
    [string[]] $Years = @('2024', '2025', '2026'),
    [switch] $RemoveUserData
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Scope -eq 'Machine' -and -not (Test-Admin)) {
    Write-Host 'Uninstalling for all users needs administrator rights. Requesting elevation...' -ForegroundColor Yellow
    $argList = @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Scope', 'Machine', '-Years', ($Years -join ','))
    if ($RemoveUserData) { $argList += '-RemoveUserData' }
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
    exit $p.ExitCode
}

$Years = @($Years | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$appData = [Environment]::GetFolderPath('ApplicationData')
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$programData = [Environment]::GetFolderPath('CommonApplicationData')
$programFiles = [Environment]::GetFolderPath('ProgramFiles')

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Warning 'Revit is running. Close Revit before uninstalling CausaFix.'
    exit 1
}

foreach ($year in $Years) {
    if ($Scope -eq 'Machine') {
        $manifest = Join-Path $programData "Autodesk\Revit\Addins\$year\CausaFix.addin"
        $bin = Join-Path $programFiles "CausaFix\$year"
    } else {
        $manifest = Join-Path $appData "Autodesk\Revit\Addins\$year\CausaFix.addin"
        $bin = Join-Path $appData "CausaFix\bin\$year"
    }
    if (Test-Path $manifest) { Remove-Item -Force $manifest; Write-Host "Revit $year : manifest removed ($Scope)." }
    if (Test-Path $bin) { Remove-Item -Recurse -Force $bin; Write-Host "Revit $year : binaries removed ($Scope)." }
}

foreach ($root in @((Join-Path $appData 'CausaFix\bin'), (Join-Path $programFiles 'CausaFix'))) {
    if ((Test-Path $root) -and -not (Get-ChildItem $root -ErrorAction SilentlyContinue)) { Remove-Item $root -ErrorAction SilentlyContinue }
}

if ($RemoveUserData) {
    foreach ($p in @((Join-Path $appData 'CausaFix'), (Join-Path $localAppData 'CausaFix'))) {
        if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "Removed $p" }
    }
} else {
    Write-Host "Profiles and settings kept in $(Join-Path $appData 'CausaFix'); reports and logs in $(Join-Path $localAppData 'CausaFix')."
}
Write-Host "CausaFixIt uninstalled ($Scope)." -ForegroundColor Cyan
