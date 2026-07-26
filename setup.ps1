#Requires -Version 7
<#
.SYNOPSIS
  One-shot setup for the window-layout kit on a new machine.

.DESCRIPTION
  - Checks PowerShell 7
  - Installs the VirtualDesktop module (CurrentUser)
  - Copies it to C:\ProgramData\PowerShell\Modules (avoids OneDrive hydration issues at logon)
  - Verifies the module loads

.EXAMPLE
  pwsh -File setup.ps1
  pwsh -File setup.ps1 -RegisterLogonTask
#>
[CmdletBinding()]
param(
  [switch]$RegisterLogonTask
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Window Layout Kit setup ===" -ForegroundColor Cyan
Write-Host "PowerShell: $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'PowerShell 7+ is required. Install from https://aka.ms/powershell'
}

# NuGet provider (needed for Install-Module on some machines)
try {
  $nuget = Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue
  if (-not $nuget -or [version]$nuget.Version -lt [version]'2.8.5.201') {
    Write-Host 'Installing NuGet package provider...'
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
  }
} catch {
  Write-Warning "NuGet provider setup: $($_.Exception.Message)"
}

Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction SilentlyContinue

Write-Host 'Installing VirtualDesktop module (CurrentUser)...'
Install-Module VirtualDesktop -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop

# Resolve installed module path (Documents or OneDrive-redirected Documents)
$mod = Get-Module VirtualDesktop -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
if (-not $mod) { throw 'VirtualDesktop module install reported success but module was not found.' }
Write-Host "Installed: $($mod.Version) at $($mod.ModuleBase)"

$dstRoot = 'C:\ProgramData\PowerShell\Modules\VirtualDesktop'
Write-Host "Copying module to local path (logon-safe): $dstRoot"
New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null
$srcRoot = Split-Path $mod.ModuleBase -Parent  # ...\VirtualDesktop
if ((Split-Path $mod.ModuleBase -Leaf) -match '^\d') {
  # ModuleBase is version folder
  Copy-Item -Path (Join-Path $srcRoot '*') -Destination $dstRoot -Recurse -Force
} else {
  Copy-Item -Path (Join-Path $mod.ModuleBase '*') -Destination $dstRoot -Recurse -Force
}

# Verify load using ProgramData first
$env:PSModulePath = "C:\ProgramData\PowerShell\Modules;$env:PSModulePath"
Import-Module VirtualDesktop -DisableNameChecking -Force -ErrorAction Stop
$desktops = @(Get-DesktopList)
Write-Host "Module OK. Current virtual desktops ($($desktops.Count)):"
$desktops | ForEach-Object { Write-Host ("  [{0}] {1}" -f $_.Number, $_.Name) }

$rules = Join-Path $PSScriptRoot 'window-layout.rules.json'
if (-not (Test-Path -LiteralPath $rules)) {
  @{
    version = 1
    capturedAt = $null
    followDesktop = $null
    startupDelaySeconds = 25
    rules = @()
  } | ConvertTo-Json | Set-Content -LiteralPath $rules -Encoding utf8
  Write-Host "Created blank rules file: $rules"
}

if ($RegisterLogonTask) {
  & (Join-Path $PSScriptRoot 'register-logon-task.ps1')
}

Write-Host ""
Write-Host "Setup complete. Next:" -ForegroundColor Green
Write-Host "  1. Arrange apps on your virtual desktops"
Write-Host "  2. pwsh -File `"$(Join-Path $PSScriptRoot 'capture-window-layout.ps1')`""
Write-Host "  3. pwsh -File `"$(Join-Path $PSScriptRoot 'apply-window-layout.ps1')`" -SkipLaunch -DelaySeconds 0"
Write-Host "  4. Optional logon: pwsh -File `"$(Join-Path $PSScriptRoot 'register-logon-task.ps1')`""
Write-Host ""
Write-Host "Emergency stop (skip apply if this file exists):"
Write-Host "  New-Item `"$(Join-Path $PSScriptRoot 'DISABLE-LAYOUT')`" -ItemType File"
