@echo off
setlocal
rem Prefer PowerShell 7 (pwsh); fall back to Windows PowerShell 5.1
set "PSEXE="
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "PSEXE=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined PSEXE if exist "%ProgramFiles%\PowerShell\7-preview\pwsh.exe" set "PSEXE=%ProgramFiles%\PowerShell\7-preview\pwsh.exe"
if not defined PSEXE if exist "%LocalAppData%\Microsoft\WindowsApps\pwsh.exe" set "PSEXE=%LocalAppData%\Microsoft\WindowsApps\pwsh.exe"
if not defined PSEXE if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" set "PSEXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not defined PSEXE (
  echo No PowerShell found on this PC.
  pause
  exit /b 1
)
"%PSEXE%" -NoProfile %*
