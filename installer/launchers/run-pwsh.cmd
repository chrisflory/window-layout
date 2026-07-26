@echo off
rem Compatibility shim — prefer PS7, else Windows PowerShell 5.1
call "%~dp0run-powershell.cmd" %*
