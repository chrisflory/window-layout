#Requires -Version 7
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
$dstRoot = 'C:\ProgramData\PowerShell\Modules\VirtualDesktop'
New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null
Copy-Item -Path (Join-Path $srcRoot '*') -Destination $dstRoot -Recurse -Force
Write-Host "Refreshed $dstRoot from $($mod.ModuleBase) (v$($mod.Version))"
