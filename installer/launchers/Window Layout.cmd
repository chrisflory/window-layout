@echo off
cd /d "%~dp0"
if exist "%~dp0WindowLayout.exe" (
  start "" "%~dp0WindowLayout.exe"
  exit /b 0
)
if exist "%~dp0Window Layout.exe" (
  start "" "%~dp0Window Layout.exe"
  exit /b 0
)
echo WindowLayout.exe not found next to this launcher.
echo Re-install Window Layout, or run Capture / Apply / List shortcuts instead.
pause
exit /b 1
