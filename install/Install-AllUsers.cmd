@echo off
rem Installs CausaFixIt for every user of this PC (asks for administrator rights).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Scope Machine %*
pause
