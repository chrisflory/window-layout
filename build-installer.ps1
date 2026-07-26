#Requires -Version 7
# Publish GUI + rebuild WindowLayoutSetup.exe (requires .NET 8 SDK + Inno Setup 6)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$guiProj = Join-Path $root 'gui\WindowLayout.Gui\WindowLayout.Gui.csproj'
$guiOut = Join-Path $root 'dist\gui'

Write-Host 'Publishing Window Layout GUI...'
dotnet publish $guiProj -c Release -o $guiOut --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$built = Join-Path $guiOut 'WindowLayout.exe'
$named = Join-Path $guiOut 'Window Layout.exe'
if (-not (Test-Path $built)) { throw "Expected publish output missing: $built" }
Copy-Item -Force $built $named
# Also drop a copy next to kit scripts for local testing
Copy-Item -Force $named (Join-Path $root 'Window Layout.exe')
Copy-Item -Force $named (Join-Path (Split-Path $root -Parent) 'Window Layout.exe') -ErrorAction SilentlyContinue

$iscc = @(
  "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe",
  'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
  'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 ISCC.exe not found. Install: winget install JRSoftware.InnoSetup' }

$iss = Join-Path $root 'installer\WindowLayout.iss'
Write-Host "Compiling installer with $iscc ..."
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }

$setup = Join-Path $root 'dist\WindowLayoutSetup.exe'
Get-Item $setup | Format-List FullName, Length, LastWriteTime
Copy-Item -Force $setup (Join-Path (Split-Path $root -Parent) 'WindowLayoutSetup.exe')
Write-Host 'Done.'
