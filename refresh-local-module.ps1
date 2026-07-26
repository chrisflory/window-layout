#Requires -Version 5.1
<#
.SYNOPSIS
  Refresh the local (ProgramData) copy of VirtualDesktop after Update-Module.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$mod = Get-Module VirtualDesktop -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
if (-not $mod) {
  throw 'VirtualDesktop not installed. Run setup.ps1 first (or Install-Module VirtualDesktop -Scope CurrentUser).'
}

$srcRoot = Split-Path $mod.ModuleBase -Parent
$versioned = (Split-Path $mod.ModuleBase -Leaf) -match '^\d'

foreach ($dstRoot in @(
  'C:\ProgramData\PowerShell\Modules\VirtualDesktop',
  'C:\ProgramData\WindowsPowerShell\Modules\VirtualDesktop'
)) {
  New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null
  if ($versioned) {
    Copy-Item -Path (Join-Path $srcRoot '*') -Destination $dstRoot -Recurse -Force
  } else {
    Copy-Item -Path (Join-Path $mod.ModuleBase '*') -Destination $dstRoot -Recurse -Force
  }
  Write-Host "Refreshed $dstRoot from $($mod.ModuleBase)"
}
