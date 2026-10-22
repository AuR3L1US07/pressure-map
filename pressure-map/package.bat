@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title PressureMap Packager

rem Build a fresh dist\PressureMap, then compile output\installer\PressureMap-<version>-setup.exe
rem and output\installer\PressureMap-<version>-portable.zip.
rem Any failure keeps the window open so the reason is visible.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" -Mode Package
set "CODE=%errorlevel%"
if not "%CODE%"=="0" (
  echo.
  echo [FAILED] Packaging stopped with exit code %CODE%. See the message above.
  pause
)
exit /b %CODE%
