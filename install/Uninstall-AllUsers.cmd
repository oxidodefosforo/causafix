@echo off
rem Removes the all-users installation of CausaFix (asks for administrator rights).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" -Scope Machine %*
pause
