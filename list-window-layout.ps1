#Requires -Version 7
# List visible top-level windows: process, virtual desktop, monitor, geometry
Import-Module VirtualDesktop -DisableNameChecking -ErrorAction Stop
Add-Type -AssemblyName System.Windows.Forms
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

$screens = [System.Windows.Forms.Screen]::AllScreens |
  Sort-Object { $_.Bounds.X }, { $_.Bounds.Y }

$rows = [System.Collections.Generic.List[object]]::new()

$callback = [EnumWins+EnumProc]{
  param([IntPtr]$hWnd, [IntPtr]$lParam)
  if (-not [EnumWins]::IsWindowVisible($hWnd)) { return $true }
  $len = [EnumWins]::GetWindowTextLength($hWnd)
  if ($len -le 0) { return $true }
  $sb = New-Object System.Text.StringBuilder ($len + 1)
  [void][EnumWins]::GetWindowText($hWnd, $sb, $sb.Capacity)
  $title = $sb.ToString()
  if ([string]::IsNullOrWhiteSpace($title)) { return $true }

  [uint32]$procId = 0
  [void][EnumWins]::GetWindowThreadProcessId($hWnd, [ref]$procId)
  try { $procName = (Get-Process -Id $procId -ErrorAction Stop).ProcessName }
  catch { $procName = '?' }

  $r = New-Object EnumWins+RECT
  [void][EnumWins]::GetWindowRect($hWnd, [ref]$r)
  $w = $r.Right - $r.Left
  $h = $r.Bottom - $r.Top
  if ($w -lt 50 -or $h -lt 50) { return $true }

  $cx = [int](($r.Left + $r.Right) / 2)
  $cy = [int](($r.Top + $r.Bottom) / 2)
  $mon = $screens | Where-Object { $_.Bounds.Contains($cx, $cy) } | Select-Object -First 1
  if ($mon) {
    $idx = ([array]::IndexOf($screens, $mon)) + 1
    $tag = if ($mon.Primary) { 'primary' } else { 'sec' }
    $monLabel = "M$idx/$tag"
  } else {
    $monLabel = '?'
  }

  try { $desk = Get-DesktopName (Get-DesktopFromWindow -Hwnd $hWnd) }
  catch { $desk = '?' }

  $state = if ([EnumWins]::IsIconic($hWnd)) { 'min' }
    elseif ([EnumWins]::IsZoomed($hWnd)) { 'max' }
    else { 'norm' }

  $short = if ($title.Length -gt 52) { $title.Substring(0, 49) + '...' } else { $title }
  $rows.Add([pscustomobject]@{
    Process = $procName
    Desktop = $desk
    Monitor = $monLabel
    X = $r.Left; Y = $r.Top; W = $w; H = $h
    State = $state
    Title = $short
  }) | Out-Null
  return $true
}

[void][EnumWins]::EnumWindows($callback, [IntPtr]::Zero)
$rows | Sort-Object Desktop, Monitor, Process | Format-Table -AutoSize
Write-Host "Monitors (left→right):"
$i = 1
foreach ($s in $screens) {
  $tag = if ($s.Primary) { 'primary' } else { 'sec' }
  Write-Host ("  M{0}/{1}: {2} origin=({3},{4}) {5}x{6}" -f $i, $tag, $s.DeviceName, $s.Bounds.X, $s.Bounds.Y, $s.Bounds.Width, $s.Bounds.Height)
  $i++
}
Write-Host ("Windows listed: {0}" -f $rows.Count)
