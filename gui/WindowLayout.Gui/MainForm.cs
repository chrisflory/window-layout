using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowLayout.Gui;

public sealed class MainForm : Form
{
    private readonly Label _nextHint = new();
    private readonly Label _statusLine = new();
    private readonly TextBox _log = new();
    private readonly SoftButton _btn1;
    private readonly SoftButton _btn2;
    private readonly SoftButton _btn3;
    private readonly Panel _advancedPanel = new();
    private readonly SoftButton _btnTheme;
    private readonly SoftButton _btnClearLog;
    private readonly SoftButton _btnOff;
    private readonly SoftButton _btnList;
    private readonly SoftButton _btnFolder;
    private readonly SoftButton _btnKill;
    private readonly SoftButton _btnClearStop;
    private readonly SoftButton _btnRepair;
    private readonly SoftButton _btnPs7;
    private readonly SoftButton _btnAbout;
    private readonly SoftButton _btnUpdate;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _versionLabel;
    private readonly Label _stepsLabel;
    private readonly Label _logHeader;
    private readonly LinkLabel _moreToggle;
    private readonly Panel _header;
    private readonly Panel _headerRule;
    private readonly Panel _bottomBar;
    private readonly Panel _steps;
    private readonly Panel _logPanel;
    private readonly Panel _logFrame;
    private readonly Panel _logInner;

    private bool _busy;
    private bool _advancedOpen;
    private bool _firstRunPending;
    private AppThemeMode _themeMode = AppThemeMode.Dark;
    private AppTheme _theme = AppTheme.Dark;
    private string? _updateTag;
    private string? _updateUrl;
    private string? _dismissedUpdateTag;

    private const string GitHubRepoUrl = "https://github.com/chrisflory/window-layout";
    private const string GitHubLatestApi = "https://api.github.com/repos/chrisflory/window-layout/releases/latest";

    private static string AppVersion =>
        typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";

    private string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private string RulesPath => Path.Combine(AppDir, "window-layout.rules.json");
    private string DisableFlag => Path.Combine(AppDir, "DISABLE-LAYOUT");
    private string UiStatePath => Path.Combine(AppDir, "ui-state.json");

    public MainForm()
    {
        Text = $"Window Layout {AppVersion}";
        Width = 760;
        Height = 640;
        MinimumSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(0);
        DoubleBuffered = true;
        RestoreWindowBounds();

        var appIcon = LoadAppIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
            ShowIcon = true;
        }

        _header = new Panel { Dock = DockStyle.Top, Height = 100 };

        if (appIcon is not null)
        {
            using var small = new Icon(appIcon, 48, 48);
            _header.Controls.Add(new PictureBox
            {
                Image = small.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(56, 56),
                Location = new Point(24, 22),
                BackColor = Color.Transparent
            });
        }

        _titleLabel = new Label
        {
            Text = "Window Layout",
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Location = new Point(appIcon is null ? 28 : 92, 22)
        };
        _header.Controls.Add(_titleLabel);

        _btnTheme = SmallButton("Light", 0, 0, _theme.BtnMuted);
        _btnTheme.Size = new Size(76, 30);
        _btnTheme.CornerRadius = 8;
        _btnTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnTheme.Click += (_, _) => ToggleTheme();

        _versionLabel = new Label
        {
            Text = $"v{AppVersion}",
            Font = new Font("Segoe UI Semibold", 10f),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        _versionLabel.Click += async (_, _) => await CheckForUpdatesAsync(interactive: true);
        _header.Controls.Add(_btnTheme);
        _header.Controls.Add(_versionLabel);
        _header.Resize += (_, _) => PlaceHeaderChrome();
        PlaceHeaderChrome();

        _subtitleLabel = new Label
        {
            Text = "Save where your windows live, then restore them — including at sign-in.",
            AutoSize = true,
            Location = new Point(appIcon is null ? 30 : 94, 58)
        };
        _header.Controls.Add(_subtitleLabel);

        _headerRule = new Panel { Dock = DockStyle.Bottom, Height = 1 };
        _header.Controls.Add(_headerRule);

        _bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(18, 0, 14, 0)
        };
        _statusLine.Dock = DockStyle.Fill;
        _statusLine.TextAlign = ContentAlignment.MiddleLeft;
        _statusLine.Text = "";
        _btnClearLog = SmallButton("Clear log", 0, 0, _theme.BtnMuted);
        _btnClearLog.Dock = DockStyle.Right;
        _btnClearLog.Width = 104;
        _btnClearLog.Height = 30;
        _btnClearLog.CornerRadius = 8;
        _btnClearLog.Click += (_, _) => { _log.Clear(); };
        _bottomBar.Controls.Add(_statusLine);
        _bottomBar.Controls.Add(_btnClearLog);

        _steps = new Panel
        {
            Dock = DockStyle.Top,
            Height = 360,
            Padding = new Padding(28, 12, 28, 8)
        };

        _nextHint.AutoSize = false;
        _nextHint.Location = new Point(28, 8);
        _nextHint.Size = new Size(680, 36);
        _nextHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _nextHint.Font = new Font("Segoe UI Semibold", 11f);
        _nextHint.Text = "Loading…";

        _stepsLabel = SectionLabel("Setup (do once, or whenever you rearrange)", 28, 48);

        _btn1 = StepButton(
            "1  Save current layout",
            "Arrange your windows first, then click. Remembers apps, desktops, and positions.",
            28, 72, _theme.AccentTeal);
        _btn2 = StepButton(
            "2  Test restore now",
            "Moves windows back to the saved layout. Try this before turning on logon.",
            28, 138, _theme.AccentGreen);
        _btn3 = StepButton(
            "3  Turn on at sign-in",
            "Runs the restore automatically after you log into Windows.",
            28, 204, _theme.AccentBlue);
        foreach (var b in new[] { _btn1, _btn2, _btn3 })
            b.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _btn1.Click += async (_, _) =>
        {
            var code = await RunScriptAsync("capture-window-layout.ps1");
            if (code == 0)
                AppendLog("Saved. Next: click “2  Test restore now” (optional but recommended).");
            RefreshUiState();
        };
        _btn2.Click += async (_, _) =>
        {
            var code = await RunScriptAsync("apply-window-layout.ps1", "-DelaySeconds", "0");
            if (code == 0)
                AppendLog("Restore finished. If it looks right, click “3  Turn on at sign-in”.");
            RefreshUiState();
        };
        _btn3.Click += async (_, _) =>
        {
            var code = await RunScriptAsync("register-logon-task.ps1");
            if (code == 0)
                AppendLog("Logon restore is on. You’re set — you can close this window.");
            RefreshUiState();
        };

        _moreToggle = new LinkLabel
        {
            Text = "More options ▸",
            AutoSize = true,
            Location = new Point(28, 272)
        };

        _advancedPanel.Location = new Point(28, 296);
        _advancedPanel.Size = new Size(680, 156);
        _advancedPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _advancedPanel.Visible = false;
        _advancedPanel.BackColor = Color.Transparent;

        _btnOff = SmallButton("Turn off sign-in restore", 0, 0, _theme.BtnMuted);
        _btnList = SmallButton("List open windows", 230, 0, _theme.BtnMuted);
        _btnFolder = SmallButton("Open files folder", 460, 0, _theme.BtnMuted);
        _btnKill = SmallButton("Emergency stop", 0, 48, _theme.BtnDanger);
        _btnClearStop = SmallButton("Clear emergency stop", 230, 48, _theme.BtnMuted);
        _btnRepair = SmallButton("Repair / setup module", 460, 48, _theme.BtnMuted);
        _btnPs7 = SmallButton("Install PowerShell 7", 0, 96, _theme.BtnDeep);
        _btnAbout = SmallButton("About / version", 230, 96, _theme.BtnMuted);
        _btnUpdate = SmallButton("Check for updates", 460, 96, _theme.BtnDeep);

        _btnOff.Click += async (_, _) =>
        {
            await RunScriptAsync("register-logon-task.ps1", "-Unregister");
            RefreshUiState();
        };
        _btnList.Click += async (_, _) => await RunScriptAsync("list-window-layout.ps1");
        _btnFolder.Click += (_, _) =>
            Process.Start(new ProcessStartInfo { FileName = AppDir, UseShellExecute = true });
        _btnKill.Click += (_, _) =>
        {
            File.WriteAllText(DisableFlag, "disabled\n");
            AppendLog("Emergency stop on — Apply and logon restore will do nothing until cleared.");
            RefreshUiState();
        };
        _btnClearStop.Click += (_, _) =>
        {
            if (File.Exists(DisableFlag)) File.Delete(DisableFlag);
            AppendLog("Emergency stop cleared.");
            RefreshUiState();
        };
        _btnRepair.Click += async (_, _) => await RunScriptAsync("setup.ps1");
        _btnPs7.Click += async (_, _) =>
        {
            if (HasPowerShell7())
            {
                MessageBox.Show(this,
                    "PowerShell 7 is already installed on this PC.",
                    "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await RunScriptAsync("install-powershell7.ps1");
        };
        _btnAbout.Click += (_, _) =>
        {
            var updateNote = _updateTag is null ? "" : $"\n\nUpdate available: {_updateTag}";
            MessageBox.Show(this,
                $"Window Layout {AppVersion}{updateNote}\n\nTheme: {_themeMode}\n\nInstall folder:\n{AppDir}\n\n{GitHubRepoUrl}",
                "About Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _btnUpdate.Click += async (_, _) => await CheckForUpdatesAsync(interactive: true);

        _advancedPanel.Controls.AddRange([
            _btnOff, _btnList, _btnFolder,
            _btnKill, _btnClearStop, _btnRepair,
            _btnPs7, _btnAbout, _btnUpdate
        ]);

        void RelayoutSteps()
        {
            var w = Math.Max(640, _steps.ClientSize.Width - 56);
            _nextHint.Width = w;
            _btn1.Width = _btn2.Width = _btn3.Width = w;
            _advancedPanel.Width = w;
            _steps.Height = _advancedOpen ? 486 : 308;
        }

        _moreToggle.LinkClicked += (_, _) =>
        {
            _advancedOpen = !_advancedOpen;
            _advancedPanel.Visible = _advancedOpen;
            _moreToggle.Text = _advancedOpen ? "More options ▾" : "More options ▸";
            RelayoutSteps();
            SaveWindowBounds();
        };

        if (_advancedOpen)
        {
            _advancedPanel.Visible = true;
            _moreToggle.Text = "More options ▾";
        }

        _steps.Controls.AddRange([
            _nextHint, _stepsLabel,
            _btn1, _btn2, _btn3,
            _moreToggle, _advancedPanel
        ]);
        _steps.Resize += (_, _) => RelayoutSteps();

        _logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 8, 28, 12)
        };
        _logHeader = new Label
        {
            Text = "Activity",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI Semibold", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.Font = new Font("Consolas", 9f);
        _log.WordWrap = true;
        _log.BorderStyle = BorderStyle.None;
        _log.Dock = DockStyle.Fill;
        _log.HideSelection = false;

        _logFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _logInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 6, 8) };
        _logInner.Controls.Add(_log);
        _logFrame.Controls.Add(_logInner);
        _logPanel.Controls.Add(_logFrame);
        _logPanel.Controls.Add(_logHeader);

        Controls.Add(_logPanel);
        Controls.Add(_steps);
        Controls.Add(_bottomBar);
        Controls.Add(_header);

        ApplyTheme();

        FormClosing += (_, _) => SaveWindowBounds();
        ResizeEnd += (_, _) => SaveWindowBounds();
        Shown += async (_, _) =>
        {
            RelayoutSteps();
            PlaceHeaderChrome();
            RefreshUiState();
            AppendLog("Arrange your windows, then follow steps 1 → 2 → 3.");
            await OfferFirstRunSetupAsync();
            await CheckForUpdatesAsync(interactive: false);
        };
    }

    private sealed class UiState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string WindowState { get; set; } = "Normal";
        public bool AdvancedOpen { get; set; }
        public bool FirstRunDone { get; set; }
        public string Theme { get; set; } = "Dark";
        public string? DismissedUpdateTag { get; set; }
    }

    private void ToggleTheme()
    {
        _themeMode = _themeMode == AppThemeMode.Dark ? AppThemeMode.Light : AppThemeMode.Dark;
        ApplyTheme();
        RefreshUiState();
        SaveWindowBounds();
    }

    private void ApplyTheme()
    {
        _theme = AppTheme.For(_themeMode);

        BackColor = _theme.BgDeep;
        ForeColor = _theme.TextPrimary;

        _header.BackColor = _theme.BgHeader;
        _headerRule.BackColor = _theme.Rule;
        _bottomBar.BackColor = _theme.BgHeader;
        _steps.BackColor = _theme.BgDeep;
        _logPanel.BackColor = _theme.BgDeep;
        _logFrame.BackColor = _theme.LogFrame;
        _logInner.BackColor = _theme.BgPanel;
        _log.BackColor = _theme.BgPanel;
        _log.ForeColor = _theme.TextSoft;

        _titleLabel.ForeColor = _theme.TextPrimary;
        _subtitleLabel.ForeColor = _theme.TextMuted;
        _versionLabel.ForeColor = _theme.TextVersion;
        _stepsLabel.ForeColor = _theme.TextMuted;
        _logHeader.ForeColor = _theme.TextMuted;
        _statusLine.ForeColor = _theme.TextMuted;

        _moreToggle.LinkColor = _theme.TextMuted;
        _moreToggle.ActiveLinkColor = _theme.AccentHint;
        _moreToggle.VisitedLinkColor = _theme.TextMuted;

        _btn1.BackColor = _theme.AccentTeal;
        _btn2.BackColor = _theme.AccentGreen;
        _btn3.BackColor = _theme.AccentBlue;
        foreach (var b in new[] { _btn1, _btn2, _btn3 })
        {
            b.ForeColor = _theme.SoftButtonFg;
            b.Invalidate();
        }

        void StyleMuted(SoftButton b, Color back)
        {
            b.BackColor = back;
            b.ForeColor = _theme.SoftButtonFg;
            b.Invalidate();
        }

        StyleMuted(_btnTheme, _theme.BtnMuted);
        StyleMuted(_btnClearLog, _theme.BtnMuted);
        StyleMuted(_btnOff, _theme.BtnMuted);
        StyleMuted(_btnList, _theme.BtnMuted);
        StyleMuted(_btnFolder, _theme.BtnMuted);
        StyleMuted(_btnClearStop, _theme.BtnMuted);
        StyleMuted(_btnRepair, _theme.BtnMuted);
        StyleMuted(_btnAbout, _theme.BtnMuted);
        StyleMuted(_btnKill, _theme.BtnDanger);
        StyleMuted(_btnPs7, _theme.BtnDeep);
        StyleMuted(_btnUpdate, _theme.BtnDeep);

        // Button shows the mode you can switch TO
        _btnTheme.Text = _themeMode == AppThemeMode.Dark ? "Light" : "Dark";
        Invalidate(true);
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (!File.Exists(UiStatePath))
            {
                _firstRunPending = true;
                return;
            }
            var state = JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath));
            if (state is null)
            {
                _firstRunPending = true;
                return;
            }

            if (Enum.TryParse<AppThemeMode>(state.Theme, ignoreCase: true, out var mode))
            {
                _themeMode = mode;
                _theme = AppTheme.For(_themeMode);
            }
            _dismissedUpdateTag = state.DismissedUpdateTag;

            if (state.Width < MinimumSize.Width || state.Height < MinimumSize.Height)
            {
                _firstRunPending = state.FirstRunDone != true;
                return;
            }

            var bounds = new Rectangle(state.X, state.Y, state.Width, state.Height);
            if (!bounds.IntersectsWith(SystemInformation.VirtualScreen))
            {
                StartPosition = FormStartPosition.CenterScreen;
                _advancedOpen = state.AdvancedOpen;
                _firstRunPending = !state.FirstRunDone;
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            if (Enum.TryParse<FormWindowState>(state.WindowState, out var ws) &&
                ws is FormWindowState.Normal or FormWindowState.Maximized)
            {
                WindowState = ws;
            }

            _advancedOpen = state.AdvancedOpen;
            _firstRunPending = !state.FirstRunDone;
            _dismissedUpdateTag = state.DismissedUpdateTag;
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
            _firstRunPending = true;
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var prevDone = false;
            try
            {
                if (File.Exists(UiStatePath))
                {
                    var prev = JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath));
                    prevDone = prev?.FirstRunDone == true;
                }
            }
            catch { /* ignore */ }

            var state = new UiState
            {
                X = r.X,
                Y = r.Y,
                Width = r.Width,
                Height = r.Height,
                WindowState = WindowState == FormWindowState.Minimized ? "Normal" : WindowState.ToString(),
                AdvancedOpen = _advancedOpen,
                FirstRunDone = prevDone || !_firstRunPending,
                Theme = _themeMode.ToString(),
                DismissedUpdateTag = _dismissedUpdateTag
            };
            File.WriteAllText(UiStatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ignore persistence errors
        }
    }

    private void MarkFirstRunDone()
    {
        _firstRunPending = false;
        SaveWindowBounds();
    }

    private async Task OfferFirstRunSetupAsync()
    {
        if (!_firstRunPending) return;

        var hasModule = HasVirtualDesktopModule();
        var hasPs7 = HasPowerShell7();

        if (hasModule)
        {
            AppendLog(hasPs7
                ? "Setup already complete (VirtualDesktop module + PowerShell 7 found)."
                : "VirtualDesktop module already installed — skipping setup prompts.");
            MarkFirstRunDone();
            return;
        }

        var result = MessageBox.Show(this,
            "Welcome to Window Layout.\n\n" +
            "Install the VirtualDesktop PowerShell module now?\n" +
            "(This is a gallery module for virtual desktops — not PowerShell 7. Needs internet.)\n\n" +
            "You can also do this later under More options → Repair / setup module.",
            "Window Layout setup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            if (!hasPs7)
            {
                var ps7 = MessageBox.Show(this,
                    "PowerShell 7 was not found.\n\nInstall it via winget? (optional — Windows PowerShell 5.1 works too)",
                    "PowerShell 7",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (ps7 == DialogResult.Yes)
                    await RunScriptAsync("install-powershell7.ps1");
            }

            await RunScriptAsync("setup.ps1");
        }

        MarkFirstRunDone();
    }

    private static bool HasVirtualDesktopModule()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var roots = new[]
            {
                Path.Combine(docs, "PowerShell", "Modules", "VirtualDesktop"),
                Path.Combine(docs, "WindowsPowerShell", "Modules", "VirtualDesktop"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PowerShell", "Modules", "VirtualDesktop"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsPowerShell", "Modules", "VirtualDesktop"),
            };
            if (roots.Any(Directory.Exists)) return true;

            foreach (var shell in new[] { "pwsh", "powershell" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = shell,
                        Arguments = "-NoProfile -Command \"if (Get-Module VirtualDesktop -ListAvailable) { '1' }\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p is null) continue;
                    var output = (p.StandardOutput.ReadToEnd() ?? "").Trim();
                    if (!p.WaitForExit(8000))
                    {
                        try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                        continue;
                    }
                    if (output.StartsWith('1')) return true;
                }
                catch
                {
                    // try next shell
                }
            }
        }
        catch
        {
            // treat as missing
        }

        return false;
    }

    private static bool HasPowerShell7()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe")
        };
        if (candidates.Any(File.Exists)) return true;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    private static Label SectionLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI Semibold", 9f)
    };

    private static SoftButton StepButton(string title, string detail, int x, int y, Color back) => new()
    {
        Text = title + "\n" + detail,
        Location = new Point(x, y),
        Size = new Size(680, 60),
        BackColor = back,
        ForeColor = Color.White,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(16, 0, 16, 0),
        Font = new Font("Segoe UI Semibold", 9.75f),
        CornerRadius = 14
    };

    private static SoftButton SmallButton(string text, int x, int y, Color back) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(210, 38),
        BackColor = back,
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 9f),
        CornerRadius = 10
    };

    private void RefreshUiState()
    {
        var rules = CountRules();
        var hasRules = rules > 0;
        var logon = IsLogonEnabled();
        var disabled = File.Exists(DisableFlag);

        _btn2.Enabled = hasRules && !disabled && !_busy;
        _btn3.Enabled = hasRules && !disabled && !_busy;
        _btn1.Enabled = !_busy;

        if (disabled)
        {
            _nextHint.Text = "Emergency stop is on — use More options → Clear emergency stop.";
            _nextHint.ForeColor = _theme.AccentWarn;
        }
        else if (!hasRules)
        {
            _nextHint.Text = "Next: arrange your windows, then click step 1 to save them.";
            _nextHint.ForeColor = _theme.AccentHint;
            Highlight(_btn1);
        }
        else if (!logon)
        {
            _nextHint.Text = "Next: click step 2 to test, then step 3 to restore at every sign-in.";
            _nextHint.ForeColor = _theme.AccentHint;
            Highlight(_btn2);
        }
        else
        {
            _nextHint.Text = "You’re set. Re-run step 1 anytime after you rearrange windows.";
            _nextHint.ForeColor = _theme.AccentOk;
            ClearHighlight();
        }

        var logonText = logon ? "sign-in restore ON" : "sign-in restore off";
        if (_updateTag is not null)
        {
            _statusLine.Text = $"  Update available: {_updateTag}  ·  click version or More options → Check for updates";
            _statusLine.ForeColor = _themeMode == AppThemeMode.Dark
                ? Color.FromArgb(250, 204, 21)
                : Color.FromArgb(180, 83, 9);
            _versionLabel.Text = $"v{AppVersion} ↑ {_updateTag}";
            _versionLabel.ForeColor = _themeMode == AppThemeMode.Dark
                ? Color.FromArgb(250, 204, 21)
                : Color.FromArgb(180, 83, 9);
        }
        else
        {
            _statusLine.Text = disabled
                ? $"  Stopped  ·  {rules} saved window(s)  ·  {logonText}"
                : $"  {rules} window(s) saved  ·  {logonText}  ·  {DateTime.Now:t}";
            _statusLine.ForeColor = disabled ? _theme.AccentWarn : _theme.TextMuted;
            _versionLabel.Text = $"v{AppVersion}";
            _versionLabel.ForeColor = _theme.TextVersion;
        }
        _versionLabel.Cursor = Cursors.Hand;
        PlaceHeaderChrome();
    }

    private void PlaceHeaderChrome()
    {
        var right = _header.ClientSize.Width - 28;
        _btnTheme.Location = new Point(Math.Max(200, right - _btnTheme.Width), 26);
        _versionLabel.Location = new Point(
            Math.Max(120, _btnTheme.Left - _versionLabel.PreferredWidth - 12), 30);
    }

    private void Highlight(SoftButton focus)
    {
        ClearHighlight();
        focus.Emphasized = true;
    }

    private void ClearHighlight()
    {
        foreach (var b in new[] { _btn1, _btn2, _btn3 })
            b.Emphasized = false;
    }

    private int CountRules()
    {
        try
        {
            if (!File.Exists(RulesPath)) return 0;
            var json = File.ReadAllText(RulesPath);
            return Regex.Matches(json, "\"process\"").Count;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsLogonEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Query /TN \"ApplyWindowLayout\" /FO LIST /V",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(4000);
            if (p?.ExitCode != 0) return false;
            return Regex.IsMatch(output, @"Status:\s*(Ready|Running)", RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private const int MaxLogLines = 500;

    // CSI / SGR color codes from PowerShell Format-Table ($PSStyle) — TextBox can't render them
    private static readonly Regex AnsiEscapeRegex = new(
        @"\u001B\[[0-9;?]*[ -/]*[@-~]|\u001B\][^\u0007]*(?:\u0007|\u001B\\)|\u001B[@-Z\\-_]",
        RegexOptions.Compiled);

    private static string StripAnsi(string text) =>
        string.IsNullOrEmpty(text) ? text : AnsiEscapeRegex.Replace(text, "");

    private void AppendLog(string line)
    {
        line = StripAnsi(line);
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _log.AppendText($"[{stamp}] {line}{Environment.NewLine}");

        var lines = _log.Lines;
        if (lines.Length > MaxLogLines)
            _log.Lines = lines.Skip(lines.Length - MaxLogLines).ToArray();

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"WindowLayout/{AppVersion}");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var resp = await client.GetAsync(GitHubLatestApi).ConfigureAwait(true);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = $"Could not check for updates (HTTP {(int)resp.StatusCode}).";
                AppendLog(msg);
                if (interactive)
                {
                    MessageBox.Show(this, msg, "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : GitHubRepoUrl;
            if (string.IsNullOrWhiteSpace(tag))
            {
                const string msg = "Could not read the latest release from GitHub.";
                AppendLog(msg);
                if (interactive)
                {
                    MessageBox.Show(this, msg, "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            var latest = ParseVersion(tag);
            var current = ParseVersion(AppVersion);
            if (latest is null || current is null)
            {
                var msg = $"Unexpected version format (latest={tag}, current={AppVersion}).";
                AppendLog(msg);
                if (interactive)
                {
                    MessageBox.Show(this, msg, "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            // Drop a stale dismiss once we're already on/past that release
            if (_dismissedUpdateTag is not null)
            {
                var dismissed = ParseVersion(_dismissedUpdateTag);
                if (dismissed is null || dismissed <= current)
                {
                    _dismissedUpdateTag = null;
                    SaveWindowBounds();
                }
            }

            if (latest <= current)
            {
                _updateTag = null;
                _updateUrl = null;
                RefreshUiState();
                AppendLog($"Up to date (v{AppVersion}).");
                if (interactive)
                {
                    MessageBox.Show(this,
                        $"You’re on the latest version ({AppVersion}).",
                        "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            _updateTag = NormalizeTag(tag);
            _updateUrl = string.IsNullOrWhiteSpace(url) ? GitHubRepoUrl : url;
            RefreshUiState();
            AppendLog($"Update available: {_updateTag} (you have v{AppVersion}).");

            // Silent startup: keep status/version/log visible; skip dialog only if user dismissed this tag
            if (!interactive &&
                string.Equals(_dismissedUpdateTag, _updateTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Manual "Check for updates" / version click always shows a result dialog
            var answer = MessageBox.Show(this,
                $"A newer version is available: {_updateTag}\n\nYou have v{AppVersion}.\n\nOpen the download page?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
            {
                _dismissedUpdateTag = null;
                SaveWindowBounds();
                Process.Start(new ProcessStartInfo { FileName = _updateUrl, UseShellExecute = true });
            }
            else
            {
                _dismissedUpdateTag = _updateTag;
                SaveWindowBounds();
            }
        }
        catch (Exception ex)
        {
            AppendLog("Update check failed: " + ex.Message);
            if (interactive)
            {
                MessageBox.Show(this,
                    "Update check failed:\n" + ex.Message,
                    "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private static string NormalizeTag(string tag) =>
        tag.StartsWith('v') || tag.StartsWith('V') ? tag : "v" + tag;

    private static Version? ParseVersion(string value)
    {
        var s = value.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        // Tags and AssemblyName versions are Major.Minor.Build — ignore any 4th component
        if (Version.TryParse(s, out var v))
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        return null;
    }

    private async Task<int> RunScriptAsync(string scriptName, params string[] extraArgs)
    {
        if (_busy) return -1;
        _busy = true;
        UseWaitCursor = true;
        _btn1.Enabled = _btn2.Enabled = _btn3.Enabled = false;
        try
        {
            var script = Path.Combine(AppDir, scriptName);
            if (!File.Exists(script))
            {
                AppendLog($"Missing script: {script}");
                return 1;
            }

            var ps = FindPowerShell();
            if (ps is null)
            {
                AppendLog("No PowerShell found on this PC.");
                MessageBox.Show(this,
                    "No PowerShell executable was found.\n\nWindows PowerShell 5.1 should be present on Windows 10/11, or install PowerShell 7 from https://aka.ms/powershell",
                    "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            AppendLog($"Running {scriptName} with {Path.GetFileName(ps)}…");

            var args = new StringBuilder();
            args.Append("-NoProfile -ExecutionPolicy Bypass -File \"").Append(script).Append('"');
            foreach (var a in extraArgs)
                args.Append(' ').Append(a);

            var psi = new ProcessStartInfo
            {
                FileName = ps,
                Arguments = args.ToString(),
                WorkingDirectory = AppDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>();
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) BeginInvoke(() => AppendLog(e.Data));
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) BeginInvoke(() => AppendLog("ERR: " + e.Data));
            };
            proc.Exited += (_, _) => tcs.TrySetResult(proc.ExitCode);

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            var code = await tcs.Task.ConfigureAwait(true);
            AppendLog(code == 0 ? "Done." : $"Finished with exit code {code}.");
            return code;
        }
        catch (Exception ex)
        {
            AppendLog("Error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            RefreshUiState();
        }
    }

    private Icon? LoadAppIcon()
    {
        try
        {
            var asm = typeof(MainForm).Assembly;
            using var stream = asm.GetManifestResourceStream("WindowLayout.Gui.app.ico");
            if (stream is not null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                using var temp = new Icon(ms);
                return (Icon)temp.Clone();
            }
        }
        catch { /* ignore */ }

        try
        {
            var path = Path.Combine(AppDir, "assets", "app.ico");
            if (File.Exists(path))
            {
                using var temp = new Icon(path);
                return (Icon)temp.Clone();
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private static string? FindPowerShell()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        foreach (var name in new[] { "pwsh", "powershell" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = name,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(3000);
                var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (line is not null && File.Exists(line)) return line;
            }
            catch { /* ignore */ }
        }

        return null;
    }
}
