#Requires -Version 5.1
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
    'WindowLayout',
    'ApplicationFrameHost', 'ShellExperienceHost',
    'SearchHost', 'StartMenuExperienceHost', 'LockApp', 'TextInputHost',
    'SystemSettings', 'dwm', 'sihost', 'RuntimeBroker'
  ),
  [StringComparer]::OrdinalIgnoreCase
)

try {

$localModules = @(
  'C:\ProgramData\PowerShell\Modules',
  'C:\ProgramData\WindowsPowerShell\Modules'
)
foreach ($localModulesPath in $localModules) {
  if ((Test-Path $localModulesPath) -and ($env:PSModulePath -notlike "*$localModulesPath*")) {
    $env:PSModulePath = "$localModulesPath;$env:PSModulePath"
  }
}
$imported = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
  try {
    Import-Module VirtualDesktop -DisableNameChecking -ErrorAction Stop
    $imported = $true
    break
  } catch {
    Write-Host "VirtualDesktop module not ready (attempt $attempt): $($_.Exception.Message)"
    Start-Sleep -Milliseconds 800
  }
}
if (-not $imported) { throw 'Could not load VirtualDesktop module after 3 attempts.' }

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
    return @(
      $TitleMatch | ForEach-Object { "$_".Trim() } | Where-Object {
        $_ -and ($_ -notmatch '^::\{')
      }
    )
  }
  $s = "$TitleMatch"
  # Shell CLSID folder paths are launch args, not window titles (e.g. Home)
  if ($s -match '^::\{') { return @() }
  if ($s -match '[,;]') {
    return @($s -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and ($_ -notmatch '^::\{') })
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

# Cache EnumWindows results briefly — VMs pay heavily for repeated full scans
$script:WinSnap = $null
$script:WinSnapAt = [datetime]::MinValue

function Invalidate-WindowSnapshot {
  $script:WinSnap = $null
  $script:WinSnapAt = [datetime]::MinValue
}

function Get-WindowSnapshot {
  param([switch]$Force)
  if (-not $Force -and $script:WinSnap -and (((Get-Date) - $script:WinSnapAt).TotalMilliseconds -lt 250)) {
    return $script:WinSnap
  }

  $hits = [System.Collections.Generic.List[object]]::new()
  $cb = [LayoutWin+EnumProc]{
    param([IntPtr]$hWnd, [IntPtr]$lParam)
    if (-not [LayoutWin]::IsWindowVisible($hWnd)) { return $true }
    [uint32]$procId = 0
    [void][LayoutWin]::GetWindowThreadProcessId($hWnd, [ref]$procId)

    $len = [LayoutWin]::GetWindowTextLength($hWnd)
    if ($len -le 0) { return $true }
    $sb = New-Object System.Text.StringBuilder ($len + 1)
    [void][LayoutWin]::GetWindowText($hWnd, $sb, $sb.Capacity)
    $title = $sb.ToString()
    # Desktop wallpaper host — never a managed Explorer folder window
    if ($title -eq 'Program Manager') { return $true }

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
  $script:WinSnap = $hits
  $script:WinSnapAt = Get-Date
  return $hits
}

function Get-MatchingWindows {
  param(
    [string]$ProcessName,
    $TitleMatch,
    [switch]$ForceRefresh
  )
  $procIds = [System.Collections.Generic.HashSet[uint32]]::new()
  foreach ($p in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
    [void]$procIds.Add([uint32]$p.Id)
  }
  if ($procIds.Count -eq 0) { return @() }

  $snap = @(Get-WindowSnapshot -Force:$ForceRefresh)
  return @($snap | Where-Object {
    $procIds.Contains([uint32]$_.ProcId) -and
    (Test-TitlePatterns -Title $_.Title -TitleMatch $TitleMatch)
  })
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
  $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSec))
  $noProcSince = $null
  $first = $true
  do {
    if (-not $first) { Invalidate-WindowSnapshot }
    $first = $false
    $procUp = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue).Count -gt 0
    if (-not $procUp) {
      if ($null -eq $noProcSince) { $noProcSince = Get-Date }
      # Don't burn the full timeout if the process never appears (common on cold VM)
      if (((Get-Date) - $noProcSince).TotalSeconds -ge 4) { return $null }
    } else {
      $noProcSince = $null
      $m = Select-BestWindow -ProcessName $ProcessName -TitleMatch $TitleMatch `
        -TargetLeft $TargetLeft -TargetTop $TargetTop -UsedHwnds $UsedHwnds
      if ($m) { return $m }
    }
    if ($TimeoutSec -le 0) { break }
    Start-Sleep -Milliseconds 200
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

  # Move (may be no-op if already on this desktop from switch-then-launch)
  try {
    Move-Window -Desktop $DesktopObj -Hwnd $win.Hwnd | Out-Null
  } catch {
    Write-Warning "Move-Window failed for $label : $($_.Exception.Message)"
  }

  Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $Rule
  Invalidate-WindowSnapshot

  # Verify; retry move up to 2 times if still wrong
  for ($i = 1; $i -le 2; $i++) {
    $actual = Get-WindowDesktopName -Hwnd $win.Hwnd
    if ($actual -eq $Rule.desktop) {
      [void]$UsedHwnds.Add([int64]$win.Hwnd)
      Write-Host "  OK hwnd=$($win.Hwnd) desktop=$actual"
      return $true
    }
    Write-Host "  Retry $i : reported desktop='$actual', want '$($Rule.desktop)'"
    Start-Sleep -Milliseconds 200
    try {
      Move-Window -Desktop $DesktopObj -Hwnd $win.Hwnd | Out-Null
    } catch {}
    Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $Rule
  }

  $final = Get-WindowDesktopName -Hwnd $win.Hwnd
  # Still claim the hwnd so the next rule doesn't keep re-targeting it
  [void]$UsedHwnds.Add([int64]$win.Hwnd)
  Write-Warning "Still on '$final' after retries for $label"
  return $false
}

# --- main ---
if (-not (Test-Path -LiteralPath $RulesFile)) {
  throw "Rules file not found: $RulesFile"
}

$doc = Get-Content -LiteralPath $RulesFile -Raw | ConvertFrom-Json
$delay = if ($null -ne $DelaySeconds) { [int]$DelaySeconds } elseif ($null -ne $doc.startupDelaySeconds) { [int]$doc.startupDelaySeconds } else { 0 }
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
$applyStarted = Get-Date

# Launch pass: one Switch-Desktop per target desktop, then start that desktop's apps.
# First windows inherit the current desktop (critical for Brave/Chrome).
if (-not $SkipLaunch) {
  Write-Host ""
  Write-Host "=== Launch pass (per desktop) ==="
  $launchClaimed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $pendingLaunch = [System.Collections.Generic.List[object]]::new()

  foreach ($rule in ($rules | Where-Object { $_.launch })) {
    $claimKey = if ($rule.process -eq 'explorer') {
      "explorer::$($rule.args)"
    } else {
      "$($rule.process)"
    }
    if (-not $launchClaimed.Add($claimKey)) { continue }

    # Chromium/Electron (Edge, Chrome, Brave, Glean, …) often keep background
    # processes with no UI. Treat "already running" as "has a matching window";
    # otherwise Start-Process so the existing instance opens/activates a window.
    Invalidate-WindowSnapshot
    if ($rule.process -eq 'explorer') {
      $already = @(Get-MatchingWindows -ProcessName 'explorer' -TitleMatch $rule.titleMatch)
      if ($already.Count -gt 0) {
        Write-Host "Explorer folder already open: $($rule.args)"
        continue
      }
    } else {
      $existingWins = @(Get-MatchingWindows -ProcessName $rule.process -TitleMatch $rule.titleMatch -ForceRefresh)
      if ($existingWins.Count -gt 0) {
        Write-Host "Already running with window: $($rule.process)"
        continue
      }
      $existingProcs = @(Get-Process -Name $rule.process -ErrorAction SilentlyContinue)
      if ($existingProcs.Count -gt 0) {
        Write-Host "Process alive but no window: $($rule.process) — launching to open UI"
      }
    }

    $pendingLaunch.Add($rule) | Out-Null
  }

  # Visit target desktops left→right (Get-DesktopList Number order), not rules-JSON
  # first-seen order — so apply walks the Task View strip once instead of hopping around.
  $pendingDeskNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($rule in $pendingLaunch) {
    [void]$pendingDeskNames.Add([string]$rule.desktop)
  }
  $desktopOrder = [System.Collections.Generic.List[string]]::new()
  foreach ($d in @(Get-DesktopList | Sort-Object { [int]$_.Number })) {
    $name = [string]$d.Name
    if ($pendingDeskNames.Contains($name) -and -not $desktopOrder.Contains($name)) {
      $desktopOrder.Add($name) | Out-Null
    }
  }
  # Any pending name missing from the list (shouldn't happen) — append first-seen
  foreach ($rule in $pendingLaunch) {
    $name = [string]$rule.desktop
    if (-not $desktopOrder.Contains($name)) {
      $desktopOrder.Add($name) | Out-Null
    }
  }

  foreach ($deskName in $desktopOrder) {
    $batch = @($pendingLaunch | Where-Object { $_.desktop -eq $deskName })
    Write-Host "Switch -> $deskName; launching $($batch.Count) app(s)..."
    Switch-Desktop -Desktop $desktopMap[$deskName] -NoAnimation | Out-Null
    Start-Sleep -Milliseconds 200
    foreach ($rule in $batch) {
      Write-Host "  launch $($rule.process) $(if ($rule.args) { $rule.args } else { '' })"
      Start-RuleProcess -Rule $rule
      Start-Sleep -Milliseconds 80
    }

    # Stay on this desktop until each app shows a real window. Chromium/Electron
    # inherit whichever desktop is current when the first window is created — if
    # we switch away too early, they land on the wrong desktop.
    $launchReady = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($rule in $batch) {
      $win = Wait-MatchingWindow -ProcessName $rule.process -TitleMatch $rule.titleMatch `
        -TargetLeft ([int]$rule.left) -TargetTop ([int]$rule.top) `
        -UsedHwnds $launchReady -TimeoutSec 45
      if ($win) {
        [void]$launchReady.Add([int64]$win.Hwnd)
        Write-Host "  ready $($rule.process) hwnd=$($win.Hwnd)"
      } else {
        Write-Host "  not ready yet: $($rule.process) (will retry in place pass)"
      }
    }
  }
}

# Place pass: quick try first (apps already up), then wait only for stragglers
Write-Host ""
Write-Host "=== Place pass ==="
$pendingPlace = [System.Collections.Generic.List[object]]::new()
foreach ($rule in $rules) {
  $ok = Place-RuleWindow -Rule $rule -DesktopObj $desktopMap[$rule.desktop] `
    -UsedHwnds $usedHwnds -TimeoutSec 0
  if (-not $ok) { $pendingPlace.Add($rule) | Out-Null }
}

if ($pendingPlace.Count -gt 0) {
  Write-Host "Waiting on $($pendingPlace.Count) window(s) still opening..."
  # Prefer LTR desktop switches for stragglers (same reason as launch pass)
  $deskNumber = @{}
  foreach ($d in @(Get-DesktopList)) {
    $deskNumber[[string]$d.Name] = [int]$d.Number
  }
  $pendingPlaceSorted = @(
    $pendingPlace | Sort-Object {
      $n = $deskNumber[[string]$_.desktop]
      if ($null -eq $n) { [int]::MaxValue } else { $n }
    }, { [string]$_.process }
  )
  foreach ($rule in $pendingPlaceSorted) {
    # Keep the target desktop current so late first-windows inherit correctly
    try {
      Switch-Desktop -Desktop $desktopMap[$rule.desktop] -NoAnimation | Out-Null
    } catch {}
    $procUp = @(Get-Process -Name $rule.process -ErrorAction SilentlyContinue).Count -gt 0
    Invalidate-WindowSnapshot
    $hasWin = @(Get-MatchingWindows -ProcessName $rule.process -TitleMatch $rule.titleMatch).Count -gt 0
    # Process alive with no UI (Chromium background apps): nudge a window open
    if (-not $SkipLaunch -and $rule.launch -and $procUp -and -not $hasWin) {
      Write-Host "  re-launch $($rule.process) (process up, no window yet)"
      Start-RuleProcess -Rule $rule
      Start-Sleep -Milliseconds 300
    } elseif (-not $SkipLaunch -and $rule.launch -and -not $procUp) {
      Write-Host "  re-launch $($rule.process) (process missing)"
      Start-RuleProcess -Rule $rule
      Start-Sleep -Milliseconds 300
      $procUp = @(Get-Process -Name $rule.process -ErrorAction SilentlyContinue).Count -gt 0
    }
    # Missing process: fail fast. Slow Electron/Chromium UIs need a longer budget.
    $timeout = if (-not $procUp) { 8 } elseif ($rule.launch) { 45 } else { 15 }
    [void](Place-RuleWindow -Rule $rule -DesktopObj $desktopMap[$rule.desktop] `
      -UsedHwnds $usedHwnds -TimeoutSec $timeout)
  }
}

# Final verification pass for anything still wrong (window recreations, late hwnds)
Write-Host ""
Write-Host "=== Verification pass ==="
Invalidate-WindowSnapshot
$usedHwnds.Clear()
foreach ($rule in $rules) {
  $win = Select-BestWindow -ProcessName $rule.process -TitleMatch $rule.titleMatch `
    -TargetLeft ([int]$rule.left) -TargetTop ([int]$rule.top) -UsedHwnds $usedHwnds
  if (-not $win) {
    # One more short wait — some apps recreate their window after first paint
    $win = Wait-MatchingWindow -ProcessName $rule.process -TitleMatch $rule.titleMatch `
      -TargetLeft ([int]$rule.left) -TargetTop ([int]$rule.top) -UsedHwnds $usedHwnds -TimeoutSec 8
  }
  if (-not $win) { continue }
  [void]$usedHwnds.Add([int64]$win.Hwnd)
  $actual = Get-WindowDesktopName -Hwnd $win.Hwnd
  $needsDesktop = ($actual -ne $rule.desktop)
  $r = New-Object LayoutWin+RECT
  [void][LayoutWin]::GetWindowRect($win.Hwnd, [ref]$r)
  $needsGeo = (
    [Math]::Abs($r.Left - [int]$rule.left) -gt 24 -or
    [Math]::Abs($r.Top - [int]$rule.top) -gt 24 -or
    [Math]::Abs(($r.Right - $r.Left) - [int]$rule.width) -gt 48 -or
    [Math]::Abs(($r.Bottom - $r.Top) - [int]$rule.height) -gt 48
  )
  if ($needsDesktop -or $needsGeo) {
    if ($needsDesktop) {
      Write-Host "Fixing $($rule.process): '$actual' -> '$($rule.desktop)'"
      try {
        Move-Window -Desktop $desktopMap[$rule.desktop] -Hwnd $win.Hwnd | Out-Null
      } catch {}
    } else {
      Write-Host "Fixing $($rule.process) geometry"
    }
    Move-WindowToGeometry -Hwnd $win.Hwnd -Rule $rule
  }
}

$elapsed = [int]((Get-Date) - $applyStarted).TotalSeconds
Write-Host "Apply finished in ${elapsed}s (excluding startup delay)."

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
