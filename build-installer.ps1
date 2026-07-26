#Requires -Version 7
<#
.SYNOPSIS
  Publish the self-contained GUI and compile WindowLayoutSetup.exe (Inno Setup).
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'gui\WindowLayout.Gui\WindowLayout.Gui.csproj'
$guiOut = Join-Path $root 'dist\gui'
$iss = Join-Path $root 'installer\WindowLayout.iss'

[xml]$csproj = Get-Content -LiteralPath $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Could not read <Version> from csproj' }

Write-Host "=== Window Layout $version (Inno Setup) ===" -ForegroundColor Cyan

$iscc = @(
  "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe",
  'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
  'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
  throw 'Inno Setup 6 ISCC.exe not found. Install: winget install JRSoftware.InnoSetup'
}

if (Test-Path $guiOut) { Remove-Item -Recurse -Force $guiOut }
New-Item -ItemType Directory -Force -Path $guiOut | Out-Null

Write-Host 'Publishing self-contained win-x64 single-file GUI...'
dotnet publish $proj -c Release -o $guiOut --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$exe = Join-Path $guiOut 'WindowLayout.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe after publish" }

Write-Host "Compiling installer with $iscc ..."
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }

$setup = Join-Path $root 'dist\WindowLayoutSetup.exe'
if (-not (Test-Path -LiteralPath $setup)) { throw "Missing $setup after compile" }

Copy-Item -Force $setup (Join-Path (Split-Path $root -Parent) 'WindowLayoutSetup.exe') -ErrorAction SilentlyContinue

Get-Item $setup | Format-List FullName, Length, LastWriteTime
Write-Host 'Done.' -ForegroundColor Green
