@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Clean.ps1" %*
exit /b %errorlevel%
