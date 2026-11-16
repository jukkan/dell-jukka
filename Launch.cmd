@echo off
if not exist "%~dp0artifacts\app\JukkasDell.exe" (
  echo Build the app first: powershell -NoProfile -File scripts\build.ps1
  pause
  exit /b 1
)
start "" "%~dp0artifacts\app\JukkasDell.exe"
