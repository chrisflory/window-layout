#Requires -Version 7
<#
.SYNOPSIS
  Capture visible windows into window-layout.rules.json for apply-window-layout.ps1

.PARAMETER OutFile
  Rules file path (default: next to this script).

.PARAMETER IncludeProcess
  Only capture these process names (optional).

.PARAMETER ExcludeProcess
  Extra process names to skip (merged with built-in skips).
#>
[CmdletBinding()]
param(
  [string]$OutFile = (Join-Path $PSScriptRoot 'window-layout.rules.json'),
  [string[]]$IncludeProcess = @(),
  [string[]]$ExcludeProcess = @()
)

$ErrorActionPreference = 'Stop'
Import-Module VirtualDesktop -DisableNameChecking -ErrorAction Stop
Add-Type -AssemblyName System.Windows.Forms

# Preserve tuning from an existing rules file (don't reset on re-capture)
$prevFollow = $null
$prevDelay = 20
if (Test-Path -LiteralPath $OutFile) {
  try {
    $prev = Get-Content -LiteralPath $OutFile -Raw | ConvertFrom-Json
    if ($null -ne $prev.followDesktop) { $prevFollow = $prev.followDesktop }
    if ($null -ne $prev.startupDelaySeconds) { $prevDelay = [int]$prev.startupDelaySeconds }
  } catch {}
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class EnumWins {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$skip = [System.Collections.Generic.HashSet[string]]::new(
  [string[]]@(
    'TextInputHost', 'ApplicationFrameHost', 'SystemSettings', 'ShellExperienceHost',
    'SearchHost', 'StartMenuExperienceHost', 'LockApp', 'dwm',
    'sihost', 'RuntimeBroker'
  ),
  [StringComparer]::OrdinalIgnoreCase
)
foreach ($x in $ExcludeProcess) { [void]$skip.Add($x) }

# Stable launch commands when the running EXE path is versioned / Store-based.
# Add your own overrides here if capture picks a bad path (e.g. versioned app folders).
$launchOverrides = @{
  Discord         = @{ Path = "$env:LOCALAPPDATA\Discord\Update.exe"; Args = '--processStart Discord.exe' }
  WindowsTerminal = @{ Path = "$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe"; Args = '' }
  olk             = @{ Path = 'shell:AppsFolder\Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows'; Args = ''; UseExplorer = $true }
  Taskmgr         = @{ Path = "$env:SystemRoot\System32\Taskmgr.exe"; Args = '' }
}

# Map Explorer window HWNDs -> folder paths (for restore)
$explorerPathByHwnd = @{}
try {
  $shellApp = New-Object -ComObject Shell.Application
  foreach ($sw in @($shellApp.Windows())) {
    try {
      if (-not $sw.HWND) { continue }
      $full = [string]$sw.FullName
      if ($full -notmatch 'explorer\.exe') { continue }
      $folderPath = $null
      try { $folderPath = [string]$sw.Document.Folder.Self.Path } catch {}
      if (-not $folderPath) {
        $url = [string]$sw.LocationURL
        if ($url -match '^file:///') {
          $folderPath = [Uri]::UnescapeDataString(($url -replace '^file:///','' -replace '/','\'))
          if ($folderPath -match '^[A-Za-z]:') { }
          elseif ($folderPath.StartsWith('\')) { $folderPath = $folderPath.TrimStart('\') }
        }
      }
      if ($folderPath) {
        $explorerPathByHwnd[[int64]$sw.HWND] = $folderPath
      }
    } catch {}
  }
} catch {
  Write-Warning "Could not enumerate Explorer folders via Shell.Application: $($_.Exception.Message)"
}

$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }, { $_.Bounds.Y }
$raw = [System.Collections.Generic.List[object]]::new()

# One WMI query up front instead of one per window (much faster)
$exeByPid = @{}
foreach ($wp in (Get-CimInstance Win32_Process -Property ProcessId, ExecutablePath)) {
  if ($wp.ExecutablePath) { $exeByPid[[uint32]$wp.ProcessId] = $wp.ExecutablePath }
}

$callback = [EnumWins+EnumProc]{
  param([IntPtr]$hWnd, [IntPtr]$lParam)
  if (-not [EnumWins]::IsWindowVisible($hWnd)) { return $true }
  if ([EnumWins]::IsIconic($hWnd)) { return $true }
  $len = [EnumWins]::GetWindowTextLength($hWnd)
  if ($len -le 0) { return $true }
  $sb = New-Object System.Text.StringBuilder ($len + 1)
  [void][EnumWins]::GetWindowText($hWnd, $sb, $sb.Capacity)
  $title = $sb.ToString()
  if ([string]::IsNullOrWhiteSpace($title)) { return $true }
  if ($title -eq 'Program Manager') { return $true }

  [uint32]$procId = 0
  [void][EnumWins]::GetWindowThreadProcessId($hWnd, [ref]$procId)
  try { $proc = Get-Process -Id $procId -ErrorAction Stop }
  catch { return $true }
  $procName = $proc.ProcessName
  if ($skip.Contains($procName)) { return $true }
  if ($IncludeProcess.Count -gt 0 -and ($IncludeProcess -notcontains $procName)) { return $true }

  $r = New-Object EnumWins+RECT
  [void][EnumWins]::GetWindowRect($hWnd, [ref]$r)
  $w = $r.Right - $r.Left
  $h = $r.Bottom - $r.Top
  if ($w -lt 80 -or $h -lt 80) { return $true }

  try { $desktop = Get-DesktopName (Get-DesktopFromWindow -Hwnd $hWnd) }
  catch { $desktop = $null }
  if ([string]::IsNullOrWhiteSpace($desktop)) { return $true }

  $exe = $exeByPid[$procId]
  $folderPath = $null
  if ($procName -eq 'explorer') {
    $folderPath = $explorerPathByHwnd[[int64]$hWnd]
  }

  $cx = [int](($r.Left + $r.Right) / 2)
  $cy = [int](($r.Top + $r.Bottom) / 2)
  $mon = $screens | Where-Object { $_.Bounds.Contains($cx, $cy) } | Select-Object -First 1
  $monIndex = if ($mon) { ([array]::IndexOf($screens, $mon)) + 1 } else { 0 }

  $raw.Add([pscustomobject]@{
    Process    = $procName
    Title      = $title
    Desktop    = $desktop
    Left       = $r.Left
    Top        = $r.Top
    Width      = $w
    Height     = $h
    Maximized  = [bool][EnumWins]::IsZoomed($hWnd)
    Path       = $exe
    FolderPath = $folderPath
    Hwnd       = [int64]$hWnd
    Monitor    = $monIndex
  }) | Out-Null
  return $true
}

[void][EnumWins]::EnumWindows($callback, [IntPtr]::Zero)

# Prefer launching these multi-window apps on a specific desktop when present.
# Edit desktop names to match YOUR virtual desktop names after you create them.
$preferredLaunchDesktop = @{
  # brave  = 'Desktop 2'
  # chrome = 'Desktop 2'
  # msedge = 'Desktop 1'
}

# Load previous launch flags by "process|desktop" so re-capture doesn't flip Brave wrong
$prevLaunch = @{}
if (Test-Path -LiteralPath $OutFile) {
  try {
    $prevDoc = Get-Content -LiteralPath $OutFile -Raw | ConvertFrom-Json
    foreach ($pr in @($prevDoc.rules)) {
      $prevLaunch["$($pr.process)|$($pr.desktop)"] = [bool]$pr.launch
    }
  } catch {}
}

# Group by process so only the first rule launches that app — except Explorer
# folder windows, where each window needs its own launch of that folder.
$launchClaimed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rules = foreach ($w in ($raw | Sort-Object Desktop, Process, Title)) {
  $titleMatch = $null
  $sameProc = @($raw | Where-Object { $_.Process -eq $w.Process })
  if ($sameProc.Count -gt 1) {
    $titleMatch = if ($w.Title -match '^(.*?)\s+-\s+') { $Matches[1] } else { $w.Title }
    if ($titleMatch.Length -gt 40) { $titleMatch = $titleMatch.Substring(0, 40) }
  }

  $path = $w.Path
  $ruleArgs = ''
  $useShell = $false
  $useExplorer = $false
  if ($launchOverrides.ContainsKey($w.Process)) {
    $o = $launchOverrides[$w.Process]
    $path = $o.Path
    $ruleArgs = $o.Args
    $useShell = [bool]$o.UseShellExecute
    $useExplorer = [bool]$o.UseExplorer
  }

  if ($w.Process -eq 'explorer') {
    $path = "$env:SystemRoot\explorer.exe"
    if ($w.FolderPath) {
      $ruleArgs = $w.FolderPath
      if (-not $titleMatch) { $titleMatch = Split-Path -Leaf $w.FolderPath }
    } else {
      $ruleArgs = ''
    }
    $doLaunch = [bool]$w.FolderPath
  } elseif ($w.Process -eq 'Taskmgr') {
    $path = "$env:SystemRoot\System32\Taskmgr.exe"
    $doLaunch = $launchClaimed.Add($w.Process)
  } elseif ($preferredLaunchDesktop.ContainsKey($w.Process) -and $sameProc.Count -gt 1) {
    # Only the preferred desktop's window gets launch:true (e.g. Brave -> Desktop 2)
    $pref = $preferredLaunchDesktop[$w.Process]
    $key = "$($w.Process)|$($w.Desktop)"
    if ($prevLaunch.ContainsKey($key)) {
      $doLaunch = [bool]$prevLaunch[$key]
      if ($doLaunch) { [void]$launchClaimed.Add($w.Process) }
    } elseif ($w.Desktop -eq $pref) {
      $doLaunch = $launchClaimed.Add($w.Process)
    } else {
      $doLaunch = $false
    }
  } else {
    $doLaunch = $launchClaimed.Add($w.Process)
  }

  [pscustomobject]@{
    id            = ("{0}-{1}" -f $w.Process, [Guid]::NewGuid().ToString('N').Substring(0, 6))
    name          = $w.Title
    process       = $w.Process
    path          = $path
    args          = $ruleArgs
    useShellExecute = $useShell
    useExplorer   = $useExplorer
    titleMatch    = $titleMatch
    desktop       = $w.Desktop
    left          = $w.Left
    top           = $w.Top
    width         = $w.Width
    height        = $w.Height
    maximized     = $w.Maximized
    launch        = $doLaunch
    enabled       = $true
  }
}

$doc = [pscustomobject]@{
  version       = 1
  capturedAt    = (Get-Date).ToString('o')
  followDesktop = $prevFollow
  startupDelaySeconds = $prevDelay
  rules         = @($rules)
}

$doc | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host "Captured $($rules.Count) rule(s) -> $OutFile"
$rules | Select-Object process, desktop, left, top, width, height, maximized, launch, titleMatch |
  Format-Table -AutoSize
