#Requires -Version 5.1
<#
.SYNOPSIS
  Register (or remove) a logon task that runs apply-window-layout.ps1

.NOTES
  The task launches run-apply-hidden.vbs via wscript.exe (window style 0).
  Direct pwsh/powershell with -WindowStyle Hidden still shows a console or
  Windows Terminal window when Terminal is the default console host.

.EXAMPLE
  powershell -File register-logon-task.ps1
  pwsh -File register-logon-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
  [switch]$Unregister,
  [string]$TaskName = 'ApplyWindowLayout'
)

$ErrorActionPreference = 'Stop'
$ApplyScript = Join-Path $PSScriptRoot 'apply-window-layout.ps1'
$HiddenVbs = Join-Path $PSScriptRoot 'run-apply-hidden.vbs'
if (-not (Test-Path -LiteralPath $ApplyScript)) {
  throw "Missing apply script: $ApplyScript"
}
if (-not (Test-Path -LiteralPath $HiddenVbs)) {
  throw "Missing silent launcher: $HiddenVbs"
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
if ($Unregister) {
  Write-Host "Removed scheduled task '$TaskName' (if it existed)."
  return
}

$wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'
if (-not (Test-Path -LiteralPath $wscript)) {
  throw "Missing wscript.exe: $wscript"
}

# //B = no script UI; //Nologo = no banner. VBS runs pwsh with SW_HIDE (style 0).
$arg = "//B //Nologo `"$HiddenVbs`""
$action = New-ScheduledTaskAction -Execute $wscript -Argument $arg
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = 'PT2S'
$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -StartWhenAvailable `
  -ExecutionTimeLimit (New-TimeSpan -Hours 1)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
  -Settings $settings -Principal $principal `
  -Description 'Restore virtual-desktop window layout after logon (silent)' | Out-Null

Write-Host "Registered '$TaskName' (at logon +2s, apply delay 3s, silent via wscript) for $env:USERNAME"
Write-Host "Launcher: $wscript $arg"
Write-Host "Apply script: $ApplyScript"
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName, State
