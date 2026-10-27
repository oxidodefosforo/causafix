@echo off
rem Installs CausaFixIt for the current user only (no administrator rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
pause
