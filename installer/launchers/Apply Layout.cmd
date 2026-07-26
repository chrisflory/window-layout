@echo off
cd /d "%~dp0"
call "%~dp0run-pwsh.cmd" -NoExit -File "%~dp0apply-window-layout.ps1" -DelaySeconds 0
