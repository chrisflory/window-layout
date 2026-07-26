@echo off
setlocal
set "PWSH="
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined PWSH if exist "%LocalAppData%\Microsoft\WindowsApps\pwsh.exe" set "PWSH=%LocalAppData%\Microsoft\WindowsApps\pwsh.exe"
if not defined PWSH (
  echo PowerShell 7 ^(pwsh^) was not found.
  echo Install it from https://aka.ms/powershell then try again.
  pause
  exit /b 1
)
"%PWSH%" -NoProfile %*
