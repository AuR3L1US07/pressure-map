@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title PressureMap Launcher

rem Daily launcher: close the old instance, rebuild only what changed, then run.
rem Use build.bat when you need the portable package in dist\PressureMap.
rem Any failure keeps the window open so the reason is visible.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run.ps1" %*
set "CODE=%errorlevel%"
if not "%CODE%"=="0" (
  echo.
  echo [FAILED] Launcher stopped with exit code %CODE%. See the message above.
  pause
)
exit /b %CODE%
