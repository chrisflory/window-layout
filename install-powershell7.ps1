#Requires -Version 5.1
<#
.SYNOPSIS
  Install PowerShell 7 via winget (optional installer task).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Test-Pwsh {
  $c = @(
    "$env:ProgramFiles\PowerShell\7\pwsh.exe",
    "$env:LocalAppData\Microsoft\WindowsApps\pwsh.exe"
  ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  return [bool]$c
}

if (Test-Pwsh) {
  Write-Host 'PowerShell 7 is already installed.'
  exit 0
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) {
  Write-Warning 'winget not found. Install PowerShell 7 manually from https://aka.ms/powershell'
  exit 1
}

Write-Host 'Installing PowerShell 7 with winget...'
& winget install --id Microsoft.PowerShell -e --accept-package-agreements --accept-source-agreements --disable-interactivity
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) {
  # -1978335189 = already installed (some winget versions)
  throw "winget install failed with exit code $LASTEXITCODE"
}

if (-not (Test-Pwsh)) {
  Write-Warning 'winget finished but pwsh.exe was not found yet. You may need to open a new terminal or reboot.'
  exit 1
}

Write-Host 'PowerShell 7 installed.'
