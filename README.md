# Window Layout

Save and restore window positions across Windows virtual desktops — with an optional restore at sign-in.

Works on Windows 10/11 **x64**. The GUI is **self-contained** (bundles .NET). Scripts run on **Windows PowerShell 5.1** (built-in) or **PowerShell 7** (preferred when available).

## Install (recommended)

1. Download **`WindowLayoutSetup.exe`** from the [latest Release](https://github.com/chrisflory/window-layout/releases/latest).
2. Run the installer (no admin required; installs under `%LOCALAPPDATA%\Programs\WindowLayout`).
3. Open **Start Menu → Window Layout**.

Installer checkboxes:

- **Install PowerShell 7** (optional, via winget) — recommended
- **Install VirtualDesktop module** (needed once, internet)
- **Restore at logon** (optional)
- **Desktop shortcut** (optional)

If you skip PowerShell 7, the built-in Windows PowerShell 5.1 is used.

## Everyday use

1. Arrange your windows (and virtual desktops) how you like them.
2. **1 — Save current layout**
3. **2 — Test restore now**
4. **3 — Turn on at sign-in** (optional)

Re-run step 1 anytime after you rearrange. Extra tools (list windows, emergency stop, repair module) are under **More options**.

Emergency stop: **More options → Emergency stop**, or create a file named `DISABLE-LAYOUT` in the install folder.

## Manual / portable (no setup.exe)

| File | Purpose |
|------|---------|
| `setup.ps1` | Install VirtualDesktop module + ProgramData copy |
| `capture-window-layout.ps1` | Snapshot open windows → `window-layout.rules.json` |
| `apply-window-layout.ps1` | Launch + place per rules |
| `list-window-layout.ps1` | Read-only inventory |
| `register-logon-task.ps1` | Install/remove at-logon task |
| `window-layout.rules.json` | Blank rules (`rules: []`) |
| `gui/` | WinForms control panel source |
| `build-installer.ps1` | Rebuild setup.exe (needs .NET 8 SDK + Inno Setup 6) |

```powershell
pwsh -File setup.ps1
# arrange apps, then:
pwsh -File capture-window-layout.ps1
pwsh -File apply-window-layout.ps1 -DelaySeconds 0
pwsh -File register-logon-task.ps1
```

## Rebuild (maintainers)

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
pwsh -File build-installer.ps1
```

Output: `dist\WindowLayoutSetup.exe`

## Privacy

Ship the installer or this kit with the blank rules file. After someone captures their layout, `window-layout.rules.json` may contain personal titles/paths — don’t redistribute that file.

## License / third party

See [THIRD-PARTY.md](THIRD-PARTY.md) for VirtualDesktop attribution.
