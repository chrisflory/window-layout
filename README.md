# Window Layout

Save and restore window positions across Windows virtual desktops — with optional restore at sign-in.

Works on Windows 10/11 **x64**. The GUI is **self-contained** (bundles .NET). Scripts run on **Windows PowerShell 5.1** (built-in) or **PowerShell 7** (preferred when available).

## Install (recommended)

1. Download **`WindowLayoutSetup.exe`** from the [latest Release](https://github.com/chrisflory/window-layout/releases/latest).
2. Run the installer wizard (no admin required).
3. Open **Start Menu → Window Layout**.

The wizard installs to `%LOCALAPPDATA%\Programs\WindowLayout`, creates Start Menu shortcuts, and offers optional desktop shortcut / VirtualDesktop module / PowerShell 7 / logon restore (desktop shortcut is **unchecked** by default).

**Pin to Start:** Right-click **Window Layout** in the Start menu → **Pin to Start**.

## Everyday use

1. Arrange your windows (and virtual desktops) how you like them.
2. **1 — Save current layout**
3. **2 — Test restore now**
4. **3 — Turn on at sign-in** (optional)

Re-run step 1 anytime after you rearrange. Extra tools are under **More options**.

Emergency stop: **More options → Emergency stop**, or create `DISABLE-LAYOUT` in the install folder.

## Rebuild (maintainers)

```powershell
pwsh -File build-installer.ps1
```

Requires .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`). Output: `dist\WindowLayoutSetup.exe`.

## Manual / portable (no Setup.exe)

| File | Purpose |
|------|---------|
| `setup.ps1` | Install VirtualDesktop module + ProgramData copy |
| `capture-window-layout.ps1` | Snapshot open windows → `window-layout.rules.json` |
| `apply-window-layout.ps1` | Launch + place per rules |
| `list-window-layout.ps1` | Read-only inventory |
| `register-logon-task.ps1` | Install/remove at-logon task |
| `gui/` | WinForms control panel source |

```powershell
pwsh -File setup.ps1
pwsh -File capture-window-layout.ps1
pwsh -File apply-window-layout.ps1 -DelaySeconds 0
```

## Privacy

Ship the installer with the blank rules file. After someone captures their layout, `window-layout.rules.json` may contain personal titles/paths — don’t redistribute that file.

## License / third party

See [THIRD-PARTY.md](THIRD-PARTY.md) for VirtualDesktop attribution.
