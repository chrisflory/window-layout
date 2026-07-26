#Requires -Version 7
<#
.SYNOPSIS
  Apply saved window-layout.rules.json: ensure desktops, launch apps, place windows.

.DESCRIPTION
  Apps inherit the CURRENT virtual desktop when they create their first window.
  Launching everything from one desktop then Move-Window
  is unreliable for Chromium/Electron. This script switches to each target
  desktop before launching that desktop's apps, then verifies placement.

.PARAMETER RulesFile
  Path to rules JSON.

.PARAMETER SkipLaunch
  Only move/resize existing windows; do not start processes.

.PARAMETER Follow
  Switch to followDesktop from the rules file (if set).

.PARAMETER DelaySeconds
  Extra wait before applying (overrides rules startupDelaySeconds when set).

.PARAMETER LogFile
  Transcript log path. Pass '' to disable logging.
#>
[CmdletBinding()]
param(
  [string]$RulesFile = (Join-Path $PSScriptRoot 'window-layout.rules.json'),
  [switch]$SkipLaunch,
  [switch]$Follow,
  [Nullable[int]]$DelaySeconds = $null,
  [string]$LogFile = (Join-Path $PSScriptRoot 'apply-window-layout.log')
)

$ErrorActionPreference = 'Stop'

# Kill switch: if this file exists next to the script, exit immediately
$disableFlag = Join-Path $PSScriptRoot 'DISABLE-LAYOUT'
if (Test-Path -LiteralPath $disableFlag) {
  Write-Host "DISABLE-LAYOUT present — skipping apply. Delete that file to re-enable."
  return
}

if ($LogFile) {
  try { Start-Transcript -Path $LogFile -Force | Out-Null } catch {}
}

# Never launch / place these (shell chrome — not File Explorer folder windows or Task Manager)
$NeverManage = [System.Collections.Generic.HashSet[string]]::new(
  [string[]]@(
    'ApplicationFrameHost', 'ShellExperienceHost',
    'SearchHost', 'StartMenuExperienceHost', 'LockApp', 'TextInputHost',
    'SystemSettings', 'dwm', 'sihost', 'RuntimeBroker'
  ),
  [StringComparer]::OrdinalIgnoreCase
)

try {

$localModules = 'C:\ProgramData\PowerShell\Modules'
if ((Test-Path $localModules) -and ($env:PSModulePath -notlike "*$localModules*")) {
  $env:PSModulePath = "$localModules;$env:PSModulePath"
}
$imported = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
  try {
    Import-Module VirtualDesktop -DisableNameChecking -ErrorAction Stop
    $imported = $true
    break
  } catch {
    Write-Host "VirtualDesktop module not ready (attempt $attempt): $($_.Exception.Message)"
    Start-Sleep -Seconds 10
  }
}
if (-not $imported) { throw 'Could not load VirtualDesktop module after 5 attempts.' }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class LayoutWin {
  public const int SW_RESTORE = 9;
  public const int SW_SHOWMAXIMIZED = 3;
  public const uint SWP_NOZORDER = 0x0004;
  public const uint SWP_NOACTIVATE = 0x0010;
  public const uint SWP_SHOWWINDOW = 0x0040;

  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint procId);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }

  [StructLayout(LayoutKind.Sequential)]
  public struct POINT { public int X, Y; }

  [StructLayout(LayoutKind.Sequential)]
  public struct WINDOWPLACEMENT {
    public int length; public int flags; public int showCmd;
    public POINT ptMinPosition; public POINT ptMaxPosition; public RECT rcNormalPosition;
  }
}
'@

function Ensure-NamedDesktop {
  param([string]$Name)
  $hit = @(Get-DesktopList) | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
  if ($hit) {
    return (Get-Desktop ([int]$hit.Number))
  }
  Write-Host "Creating desktop '$Name'..."
  return (New-Desktop | Set-DesktopName -Name $Name -PassThru)
}

function Get-TitlePatterns {
  param($TitleMatch)
  if ($null -eq $TitleMatch -or $TitleMatch -eq '') { return @() }
  # Allow string, array, or comma/semicolon-separated list
  if ($TitleMatch -is [System.Array]) {
    return @($TitleMatch | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
  }
  $s = "$TitleMatch"
  if ($s -match '[,;]') {
    return @($s -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  }
  return @($s)
}

function Test-TitlePatterns {
  param(
    [string]$Title,
    $TitleMatch
  )
  $patterns = @(Get-TitlePatterns -TitleMatch $TitleMatch)
  if ($patterns.Count -eq 0) { return $true }
  foreach ($p in $patterns) {
    if ($Title -like "*$p*") { return $true }
  }
  return $false
}

function Get-MatchingWindows {
  param(
    [string]$ProcessName,
    $TitleMatch
  )
  $procIds = [System.Collections.Generic.HashSet[uint32]]::new()
  foreach ($p in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
    [void]$procIds.Add([uint32]$p.Id)
  }
  if ($procIds.Count -eq 0) { return @() }

  $hits = [System.Collections.Generic.List[object]]::new()
  $cb = [LayoutWin+EnumProc]{
    param([IntPtr]$hWnd, [IntPtr]$lParam)
    if (-not [LayoutWin]::IsWindowVisible($hWnd)) { return $true }
    [uint32]$procId = 0
    [void][LayoutWin]::GetWindowThreadProcessId($hWnd, [ref]$procId)
    if (-not $procIds.Contains($procId)) { return $true }

    $len = [LayoutWin]::GetWindowTextLength($hWnd)
    if ($len -le 0) { return $true }
    $sb = New-Object System.Text.StringBuilder ($len + 1)
    [void][LayoutWin]::GetWindowText($hWnd, $sb, $sb.Capacity)
    $title = $sb.ToString()
    if (-not (Test-TitlePatterns -Title $title -TitleMatch $TitleMatch)) { return $true }

    $iconic = [LayoutWin]::IsIconic($hWnd)
    $r = New-Object LayoutWin+RECT
    if ($iconic) {
      $wp = New-Object LayoutWin+WINDOWPLACEMENT
      $wp.length = [System.Runtime.InteropServices.Marshal]::SizeOf($wp)
      [void][LayoutWin]::GetWindowPlacement($hWnd, [ref]$wp)
      $r = $wp.rcNormalPosition
    } else {
      [void][LayoutWin]::GetWindowRect($hWnd, [ref]$r)
      if (($r.Right - $r.Left) -lt 50 -or ($r.Bottom - $r.Top) -lt 50) { return $true }
    }

    $hits.Add([pscustomobject]@{
      Hwnd = $hWnd
      Title = $title
      ProcId = $procId
      Iconic = $iconic
      Left = $r.Left
      Top = $r.Top
    }) | Out-Null
    return $true
  }
  [void][LayoutWin]::EnumWindows($cb, [IntPtr]::Zero)
  return $hits
}

function Select-BestWindow {
  param(
    [string]$ProcessName,
    $TitleMatch,
    [int]$TargetLeft,
    [int]$TargetTop,
    [System.Collections.Generic.HashSet[long]]$UsedHwnds
  )
  # Prefer title matches; fall back to nearest unused window of this process
  $all = @(Get-MatchingWindows -ProcessName $ProcessName -TitleMatch $null)
  $available = @($all | Where-Object { -not $UsedHwnds.Contains([int64]$_.Hwnd) })
  if ($available.Count -eq 0) { return $null }

  $patterns = @(Get-TitlePatterns -TitleMatch $TitleMatch)
  if ($patterns.Count -gt 0) {
    $titled = @($available | Where-Object { Test-TitlePatterns -Title $_.Title -TitleMatch $TitleMatch })
    if ($titled.Count -gt 0) {
      return $titled |
        Sort-Object { [Math]::Abs($_.Left - $TargetLeft) + [Math]::Abs($_.Top - $TargetTop) } |
        Select-Object -First 1
    }
  }

  return $available |
    Sort-Object { [Math]::Abs($_.Left - $TargetLeft) + [Math]::Abs($_.Top - $TargetTop) } |
    Select-Object -First 1
}

function Wait-MatchingWindow {
  param(
    [string]$ProcessName,
    [string]$TitleMatch,
    [int]$TargetLeft,
    [int]$TargetTop,
    [System.Collections.Generic.HashSet[long]]$UsedHwnds,
    [int]$TimeoutSec = 60
  )
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  do {
    $m = Select-BestWindow -ProcessName $ProcessName -TitleMatch $TitleMatch `
      -TargetLeft $TargetLeft -TargetTop $TargetTop -UsedHwnds $UsedHwnds
    if ($m) { return $m }
    Start-Sleep -Milliseconds 400
  } while ((Get-Date) -lt $deadline)
  return $null
}

function Start-RuleProcess {
  param($Rule)
  if ($NeverManage.Contains("$($Rule.process)")) {
    Write-Host "Refusing to launch blocked process: $($Rule.process)"
    return
  }
  if (-not $Rule.path) {
    Write-Warning "No path for $($Rule.process); skip launch"
    return
  }
  # Outlook / Store apps use explorer.exe only as a launcher for shell:AppsFolder\...
  if ($Rule.useExplorer -or ($Rule.path -like 'shell:AppsFolder\*')) {
    Start-Process -FilePath explorer.exe -ArgumentList $Rule.path | Out-Null
    return
  }
  # File Explorer folder window: explorer.exe "C:\path"
  if ($Rule.process -eq 'explorer') {
    if ($Rule.args) {
      Start-Process -FilePath explorer.exe -ArgumentList "`"$($Rule.args)`"" | Out-Null
    } else {
      Write-Warning "Explorer rule has no folder path; place-only"
    }
    return
  }
  if ($Rule.useShellExecute -or ($Rule.path -match '^[a-z]+:')) {
    Start-Process -FilePath $Rule.path | Out-Null
    return
  }
  if (-not (Test-Path -LiteralPath $Rule.path)) {
    Write-Warning "Path missing for $($Rule.process): $($Rule.path)"
    return
  }
  if ($Rule.args) {
    Start-Process -FilePath $Rule.path -ArgumentList $Rule.args | Out-Null
  } else {
    Start-Process -FilePath $Rule.path | Out-Null
  }
}

function Move-WindowToGeometry {
  param(
    [IntPtr]$Hwnd,
    $Rule
  )
  $left = [int]$Rule.left
  $top = [int]$Rule.top
  $width = [Math]::Max(200, [int]$Rule.width)
  $height = [Math]::Max(200, [int]$Rule.height)

  [void][LayoutWin]::ShowWindow($Hwnd, [LayoutWin]::SW_RESTORE)
  [void][LayoutWin]::SetWindowPos(
    $Hwnd, [IntPtr]::Zero, $left, $top, $width, $height,
    [LayoutWin]::SWP_NOZORDER -bor [LayoutWin]::SWP_NOACTIVATE -bor [LayoutWin]::SWP_SHOWWINDOW
  )

  if ($Rule.maximized) {
    [void][LayoutWin]::ShowWindow($Hwnd, [LayoutWin]::SW_SHOWMAXIMIZED)
  }
}

function Get-WindowDesktopName {
  param([IntPtr]$Hwnd)
  try {
    return (Get-DesktopName (Get-DesktopFromWindow -Hwnd $Hwnd))
  } catch {
    return $null
  }
}

function Place-RuleWindow {
  param(
    $Rule,
    $DesktopObj,
    [System.Collections.Generic.HashSet[long]]$UsedHwnds,
    [int]$TimeoutSec
  )
  $label = if ($Rule.titleMatch) { "$($Rule.process) [$($Rule.titleMatch)]" } else { $Rule.process }
  Write-Host "Placing $label -> desktop '$($Rule.desktop)' ($($Rule.left),$($Rule.top) $($Rule.width)x$($Rule.height))..."

  $win = Wait-MatchingWindow -ProcessName $Rule.process -TitleMatch $Rule.titleMatch `
    -TargetLeft ([int]$Rule.left) -TargetTop ([int]$Rule.top) -UsedHwnds $UsedHwnds -TimeoutSec $TimeoutSec
  if (-not $win) {
    Write-Warning "No window found for $label (waited ${TimeoutSec}s)"
    return $false
  }

  [void]$UsedHwnds.Add([int64]$win.Hwnd)

  # Move (may be no-op if already on this desktop from switch-then-launch)
  try {
    Move-Window -Desktop $DesktopObj -Hwnd $win.Hwnd | Out-Null
  } catch {
    Write-Warning "Move-Window failed for $label : $($_.Exception.Message)"
  }

  Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $Rule

  # Verify; retry move up to 3 times if still wrong
  for ($i = 1; $i -le 3; $i++) {
    $actual = Get-WindowDesktopName -Hwnd $win.Hwnd
    if ($actual -eq $Rule.desktop) {
      Write-Host "  OK hwnd=$($win.Hwnd) desktop=$actual"
      return $true
    }
    Write-Host "  Retry $i : reported desktop='$actual', want '$($Rule.desktop)'"
    Start-Sleep -Milliseconds 500
    try {
      Move-Window -Desktop $DesktopObj -Hwnd $win.Hwnd | Out-Null
    } catch {}
    Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $Rule
  }

  $final = Get-WindowDesktopName -Hwnd $win.Hwnd
  Write-Warning "Still on '$final' after retries for $label"
  return $false
}

# --- main ---
if (-not (Test-Path -LiteralPath $RulesFile)) {
  throw "Rules file not found: $RulesFile"
}

$doc = Get-Content -LiteralPath $RulesFile -Raw | ConvertFrom-Json
$delay = if ($null -ne $DelaySeconds) { [int]$DelaySeconds } else { [int]($doc.startupDelaySeconds ?? 0) }
if ($delay -gt 0) {
  Write-Host "Waiting ${delay}s before applying layout..."
  Start-Sleep -Seconds $delay
}

$rules = @($doc.rules | Where-Object {
  $_.enabled -ne $false -and -not $NeverManage.Contains("$($_.process)")
})
if ($rules.Count -eq 0) {
  Write-Host 'No enabled rules.'
  return
}

$startDesktop = Get-CurrentDesktop

# Ensure all target desktops exist
$desktopMap = @{}
foreach ($d in ($rules.desktop | Select-Object -Unique)) {
  $desktopMap[$d] = Ensure-NamedDesktop -Name $d
}

$usedHwnds = [System.Collections.Generic.HashSet[long]]::new()

# Launch pass: switch to EACH app's target desktop before starting it,
# so first windows inherit the right desktop (critical for Brave/Chrome).
if (-not $SkipLaunch) {
  Write-Host ""
  Write-Host "=== Launch pass (per-app desktop) ==="
  $launchClaimed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($rule in ($rules | Where-Object { $_.launch })) {
    # Explorer: each folder window is a separate launch (keyed by folder path)
    # Everyone else: one launch per process name
    $claimKey = if ($rule.process -eq 'explorer') {
      "explorer::$($rule.args)"
    } else {
      "$($rule.process)"
    }
    if (-not $launchClaimed.Add($claimKey)) { continue }

    if ($rule.process -eq 'explorer') {
      # Explorer is always "running"; still open this folder if not already visible
      $already = @(Get-MatchingWindows -ProcessName 'explorer' -TitleMatch $rule.titleMatch)
      if ($already.Count -gt 0) {
        Write-Host "Explorer folder already open: $($rule.args)"
        continue
      }
    } else {
      $existing = @(Get-Process -Name $rule.process -ErrorAction SilentlyContinue)
      if ($existing.Count -gt 0 -and $rule.process -ne 'Taskmgr') {
        # Taskmgr: process may exist without visible window; still try launch
        # Other apps: skip if process already running
        Write-Host "Already running: $($rule.process)"
        continue
      }
      if ($rule.process -eq 'Taskmgr') {
        $tmWin = @(Get-MatchingWindows -ProcessName 'Taskmgr' -TitleMatch $null)
        if ($tmWin.Count -gt 0) {
          Write-Host 'Already running: Taskmgr'
          continue
        }
      }
    }

    Write-Host "Switch -> $($rule.desktop); launching $($rule.process) $(if ($rule.args) { $rule.args } else { '' })..."
    Switch-Desktop -Desktop $desktopMap[$rule.desktop] -NoAnimation | Out-Null
    Start-Sleep -Milliseconds 600
    Start-RuleProcess -Rule $rule
  }
  Write-Host "Waiting for launched apps to create windows..."
  Start-Sleep -Seconds 10
}

# Place pass: match by title keywords when set, then geometry
Write-Host ""
Write-Host "=== Place pass ==="
foreach ($rule in $rules) {
  $procUp = @(Get-Process -Name $rule.process -ErrorAction SilentlyContinue).Count -gt 0
  $timeout = if (-not $procUp) { 8 } elseif ($rule.launch) { 90 } else { 45 }
  [void](Place-RuleWindow -Rule $rule -DesktopObj $desktopMap[$rule.desktop] -UsedHwnds $usedHwnds -TimeoutSec $timeout)
}

# Final verification pass for anything still wrong (window recreations, late hwnds)
Write-Host ""
Write-Host "=== Verification pass ==="
Start-Sleep -Seconds 3
$usedHwnds.Clear()
foreach ($rule in $rules) {
  $win = Select-BestWindow -ProcessName $rule.process -TitleMatch $rule.titleMatch `
    -TargetLeft ([int]$rule.left) -TargetTop ([int]$rule.top) -UsedHwnds $usedHwnds
  if (-not $win) { continue }
  [void]$usedHwnds.Add([int64]$win.Hwnd)
  $actual = Get-WindowDesktopName -Hwnd $win.Hwnd
  if ($actual -ne $rule.desktop) {
    Write-Host "Fixing $($rule.process): '$actual' -> '$($rule.desktop)'"
    try {
      Move-Window -Desktop $desktopMap[$rule.desktop] -Hwnd $win.Hwnd | Out-Null
    } catch {}
    Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $rule
  }
}

# Return to followDesktop, or the desktop we started on
if ($Follow -and $doc.followDesktop) {
  Write-Host "Switching to '$($doc.followDesktop)'..."
  Switch-Desktop -Desktop $doc.followDesktop -NoAnimation | Out-Null
} elseif ($startDesktop) {
  Write-Host "Returning to starting desktop..."
  Switch-Desktop -Desktop $startDesktop -NoAnimation | Out-Null
}

Write-Host 'Layout apply complete.'

} finally {
  if ($LogFile) {
    try { Stop-Transcript | Out-Null } catch {}
  }
}
