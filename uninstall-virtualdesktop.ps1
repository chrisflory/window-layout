#Requires -Version 5.1
<#
.SYNOPSIS
  Remove VirtualDesktop module copies installed by Window Layout setup.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

Write-Host 'Removing VirtualDesktop PowerShell module...'

try {
  $mods = @(Get-Module VirtualDesktop -ListAvailable -ErrorAction SilentlyContinue)
  if ($mods.Count -gt 0) {
    Uninstall-Module VirtualDesktop -AllVersions -Force -ErrorAction SilentlyContinue
  }
} catch {
  Write-Warning "Uninstall-Module: $($_.Exception.Message)"
}

# Gallery CurrentUser leftovers + ProgramData copies created by setup.ps1
$docs = [Environment]::GetFolderPath('MyDocuments')
$roots = @(
  (Join-Path $docs 'PowerShell\Modules\VirtualDesktop'),
  (Join-Path $docs 'WindowsPowerShell\Modules\VirtualDesktop'),
  'C:\ProgramData\PowerShell\Modules\VirtualDesktop',
  'C:\ProgramData\WindowsPowerShell\Modules\VirtualDesktop'
)

foreach ($root in $roots) {
  if ($root -and (Test-Path -LiteralPath $root)) {
    Write-Host "Deleting $root"
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
  }
}

$left = @(Get-Module VirtualDesktop -ListAvailable -ErrorAction SilentlyContinue)
if ($left.Count -eq 0) {
  Write-Host 'VirtualDesktop module removed.'
} else {
  Write-Warning ("VirtualDesktop still listed: " + (($left | ForEach-Object { $_.ModuleBase }) -join '; '))
}
