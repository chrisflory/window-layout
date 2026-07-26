#Requires -Version 7
<#
.SYNOPSIS
  Register (or remove) a logon task that runs apply-window-layout.ps1

.EXAMPLE
  pwsh -File register-logon-task.ps1
  pwsh -File register-logon-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
  [switch]$Unregister,
  [string]$TaskName = 'ApplyWindowLayout'
)

$ErrorActionPreference = 'Stop'
$ApplyScript = Join-Path $PSScriptRoot 'apply-window-layout.ps1'
if (-not (Test-Path -LiteralPath $ApplyScript)) {
  throw "Missing apply script: $ApplyScript"
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
if ($Unregister) {
  Write-Host "Removed scheduled task '$TaskName' (if it existed)."
  return
}

$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ApplyScript`""
$action = New-ScheduledTaskAction -Execute $pwsh -Argument $arg
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = 'PT15S'
$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -StartWhenAvailable `
  -ExecutionTimeLimit (New-TimeSpan -Hours 1)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
  -Settings $settings -Principal $principal `
  -Description 'Restore virtual-desktop window layout after logon' | Out-Null

Write-Host "Registered '$TaskName' (at logon +15s) for $env:USERNAME"
Write-Host "Apply script: $ApplyScript"
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName, State
