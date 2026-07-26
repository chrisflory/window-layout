@echo off
cd /d "%~dp0"
call "%~dp0run-pwsh.cmd" -NoExit -File "%~dp0list-window-layout.ps1"
