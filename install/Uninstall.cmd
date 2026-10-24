@echo off
rem Removes the current user installation of CausaFix. Profiles and settings are kept.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
pause
