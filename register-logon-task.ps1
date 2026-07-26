#Requires -Version 5.1
<#
.SYNOPSIS
  Register (or remove) a logon task that runs apply-window-layout.ps1

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
if (-not (Test-Path -LiteralPath $ApplyScript)) {
  throw "Missing apply script: $ApplyScript"
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
if ($Unregister) {
  Write-Host "Removed scheduled task '$TaskName' (if it existed)."
  return
}

function Get-LayoutPowerShell {
  $candidates = @(
    "$env:ProgramFiles\PowerShell\7\pwsh.exe",
    "$env:LocalAppData\Microsoft\WindowsApps\pwsh.exe",
    "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  )
  foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c) { return $c }
  }
  $cmd = Get-Command pwsh, powershell -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cmd) { return $cmd.Source }
  throw 'No PowerShell executable found.'
}

$psExe = Get-LayoutPowerShell
$arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ApplyScript`""
$action = New-ScheduledTaskAction -Execute $psExe -Argument $arg
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
Write-Host "Engine: $psExe"
Write-Host "Apply script: $ApplyScript"
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName, State
