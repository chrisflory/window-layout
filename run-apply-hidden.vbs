' Window Layout - silent launcher for the ApplyWindowLayout logon task.
' WScript.Shell.Run with window style 0 avoids a visible console / Windows Terminal
' window (pwsh -WindowStyle Hidden alone is not enough when Terminal is the default host).
Option Explicit

Dim sh, fso, dir, ps, script, cmd, rc
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
script = dir & "\apply-window-layout.ps1"

If Not fso.FileExists(script) Then
  WScript.Quit 2
End If

ps = ""
If fso.FileExists(sh.ExpandEnvironmentStrings("%ProgramFiles%\PowerShell\7\pwsh.exe")) Then
  ps = sh.ExpandEnvironmentStrings("%ProgramFiles%\PowerShell\7\pwsh.exe")
ElseIf fso.FileExists(sh.ExpandEnvironmentStrings("%ProgramFiles%\PowerShell\7-preview\pwsh.exe")) Then
  ps = sh.ExpandEnvironmentStrings("%ProgramFiles%\PowerShell\7-preview\pwsh.exe")
ElseIf fso.FileExists(sh.ExpandEnvironmentStrings("%LocalAppData%\Microsoft\WindowsApps\pwsh.exe")) Then
  ps = sh.ExpandEnvironmentStrings("%LocalAppData%\Microsoft\WindowsApps\pwsh.exe")
ElseIf fso.FileExists(sh.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")) Then
  ps = sh.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
End If

If ps = "" Then
  WScript.Quit 1
End If

' Keep -WindowStyle Hidden as a belt-and-suspenders; style 0 is what actually hides the window.
' -Logon: conservative settle/place; never create/reorder virtual desktops.
cmd = """" & ps & """ -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & script & """ -Logon -DelaySeconds 5"
' 0 = hidden, True = wait so Task Scheduler Last Run Result reflects apply exit code
rc = sh.Run(cmd, 0, True)
WScript.Quit rc