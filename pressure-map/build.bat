@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" -Mode Build
exit /b %errorlevel%
