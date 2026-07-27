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
    private readonly Button _btn1;
    private readonly Button _btn2;
    private readonly Button _btn3;
    private readonly Panel _advancedPanel = new();
    private bool _busy;
    private bool _advancedOpen;
    private bool _firstRunPending;

    private const string GitHubRepoUrl = "https://github.com/chrisflory/window-layout";

    private string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private string RulesPath => Path.Combine(AppDir, "window-layout.rules.json");
    private string DisableFlag => Path.Combine(AppDir, "DISABLE-LAYOUT");
    private string UiStatePath => Path.Combine(AppDir, "ui-state.json");

    public MainForm()
    {
        Text = "Window Layout";
        Width = 760;
        Height = 640;
        MinimumSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 18, 32);
        ForeColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(0);
        RestoreWindowBounds();

        var appIcon = LoadAppIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
            ShowIcon = true;
        }

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.FromArgb(15, 23, 42)
        };

        if (appIcon is not null)
        {
            using var small = new Icon(appIcon, 48, 48);
            var logo = new PictureBox
            {
                Image = small.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(56, 56),
                Location = new Point(24, 20),
                BackColor = Color.Transparent
            };
            header.Controls.Add(logo);
        }

        header.Controls.Add(new Label
        {
            Text = "Window Layout",
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Location = new Point(appIcon is null ? 28 : 92, 20),
            ForeColor = Color.White
        });
        header.Controls.Add(new Label
        {
            Text = "Save where your windows live, then restore them — including at sign-in.",
            AutoSize = true,
            Location = new Point(appIcon is null ? 30 : 94, 56),
            ForeColor = Color.FromArgb(148, 163, 184)
        });

        // Fixed bottom bar — status + clear activity
        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(16, 0, 12, 0)
        };
        _statusLine.Dock = DockStyle.Fill;
        _statusLine.TextAlign = ContentAlignment.MiddleLeft;
        _statusLine.ForeColor = Color.FromArgb(148, 163, 184);
        _statusLine.Text = "";
        var btnClearLog = new Button
        {
            Text = "Clear log",
            Dock = DockStyle.Right,
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
            Margin = new Padding(8)
        };
        btnClearLog.Click += (_, _) => { _log.Clear(); };
        bottomBar.Controls.Add(_statusLine);
        bottomBar.Controls.Add(btnClearLog);

        // Top actions (fixed height); activity log fills the rest and scrolls
        var steps = new Panel
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
        _nextHint.ForeColor = Color.FromArgb(103, 232, 249);
        _nextHint.Text = "Loading…";

        var stepsLabel = SectionLabel("Setup (do once, or whenever you rearrange)", 28, 48);

        _btn1 = StepButton(
            "1  Save current layout",
            "Arrange your windows first, then click. Remembers apps, desktops, and positions.",
            28, 72, Color.FromArgb(8, 145, 178));
        _btn2 = StepButton(
            "2  Test restore now",
            "Moves windows back to the saved layout. Try this before turning on logon.",
            28, 138, Color.FromArgb(5, 150, 105));
        _btn3 = StepButton(
            "3  Turn on at sign-in",
            "Runs the restore automatically after you log into Windows.",
            28, 204, Color.FromArgb(37, 99, 235));
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

        var moreToggle = new LinkLabel
        {
            Text = "More options ▸",
            AutoSize = true,
            Location = new Point(28, 272),
            LinkColor = Color.FromArgb(148, 163, 184),
            ActiveLinkColor = Color.FromArgb(103, 232, 249),
            VisitedLinkColor = Color.FromArgb(148, 163, 184)
        };

        _advancedPanel.Location = new Point(28, 296);
        _advancedPanel.Size = new Size(680, 148);
        _advancedPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _advancedPanel.Visible = false;

        var btnOff = SmallButton("Turn off sign-in restore", 0, 0, Color.FromArgb(71, 85, 105));
        var btnList = SmallButton("List open windows", 230, 0, Color.FromArgb(71, 85, 105));
        var btnFolder = SmallButton("Open files folder", 460, 0, Color.FromArgb(71, 85, 105));
        var btnKill = SmallButton("Emergency stop", 0, 48, Color.FromArgb(185, 28, 28));
        var btnClear = SmallButton("Clear emergency stop", 230, 48, Color.FromArgb(71, 85, 105));
        var btnRepair = SmallButton("Repair / setup module", 460, 48, Color.FromArgb(71, 85, 105));
        var btnPs7 = SmallButton("Install PowerShell 7", 0, 96, Color.FromArgb(30, 64, 175));
        var btnAbout = SmallButton("About / version", 230, 96, Color.FromArgb(71, 85, 105));

        btnOff.Click += async (_, _) =>
        {
            await RunScriptAsync("register-logon-task.ps1", "-Unregister");
            RefreshUiState();
        };
        btnList.Click += async (_, _) => await RunScriptAsync("list-window-layout.ps1");
        btnFolder.Click += (_, _) =>
            Process.Start(new ProcessStartInfo { FileName = AppDir, UseShellExecute = true });
        btnKill.Click += (_, _) =>
        {
            File.WriteAllText(DisableFlag, "disabled\n");
            AppendLog("Emergency stop on — Apply and logon restore will do nothing until cleared.");
            RefreshUiState();
        };
        btnClear.Click += (_, _) =>
        {
            if (File.Exists(DisableFlag)) File.Delete(DisableFlag);
            AppendLog("Emergency stop cleared.");
            RefreshUiState();
        };
        btnRepair.Click += async (_, _) => await RunScriptAsync("setup.ps1");
        btnPs7.Click += async (_, _) =>
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
        btnAbout.Click += (_, _) =>
        {
            var ver = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
            MessageBox.Show(this,
                $"Window Layout {ver}\n\nInstall folder:\n{AppDir}\n\n{GitHubRepoUrl}",
                "About Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _advancedPanel.Controls.AddRange([
            btnOff, btnList, btnFolder,
            btnKill, btnClear, btnRepair,
            btnPs7, btnAbout
        ]);

        void RelayoutSteps()
        {
            var w = Math.Max(640, steps.ClientSize.Width - 56);
            _nextHint.Width = w;
            _btn1.Width = _btn2.Width = _btn3.Width = w;
            _advancedPanel.Width = w;
            steps.Height = _advancedOpen ? 470 : 300;
        }

        moreToggle.LinkClicked += (_, _) =>
        {
            _advancedOpen = !_advancedOpen;
            _advancedPanel.Visible = _advancedOpen;
            moreToggle.Text = _advancedOpen ? "More options ▾" : "More options ▸";
            RelayoutSteps();
            SaveWindowBounds();
        };

        if (_advancedOpen)
        {
            _advancedPanel.Visible = true;
            moreToggle.Text = "More options ▾";
        }

        steps.Controls.AddRange([
            _nextHint, stepsLabel,
            _btn1, _btn2, _btn3,
            moreToggle, _advancedPanel
        ]);
        steps.Resize += (_, _) => RelayoutSteps();

        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 4, 28, 8)
        };
        var logHeader = new Label
        {
            Text = "Activity",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(15, 23, 42);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = new Font("Consolas", 9f);
        _log.WordWrap = true;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Dock = DockStyle.Fill;
        _log.HideSelection = false;
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(logHeader);

        // Dock order: Fill first in z-order terms — add Fill before Top so Top gets priority... 
        // WinForms: last docked control gets preference for remaining space when using Fill.
        // Correct order: add Fill last.
        Controls.Add(logPanel);
        Controls.Add(steps);
        Controls.Add(bottomBar);
        Controls.Add(header);

        FormClosing += (_, _) => SaveWindowBounds();
        ResizeEnd += (_, _) => SaveWindowBounds();
        Shown += async (_, _) =>
        {
            RelayoutSteps();
            RefreshUiState();
            AppendLog("Arrange your windows, then follow steps 1 → 2 → 3.");
            await OfferFirstRunSetupAsync();
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
            if (state is null || state.Width < MinimumSize.Width || state.Height < MinimumSize.Height)
            {
                _firstRunPending = state?.FirstRunDone != true;
                return;
            }

            var bounds = new Rectangle(state.X, state.Y, state.Width, state.Height);
            if (!bounds.IntersectsWith(SystemInformation.VirtualScreen))
            {
                StartPosition = FormStartPosition.CenterScreen;
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
                FirstRunDone = prevDone || !_firstRunPending
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

        // Already set up (common after upgrade / reinstall) — don't nag
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

            // Fall back to Get-Module (covers OneDrive / custom PSModulePath)
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
        ForeColor = Color.FromArgb(148, 163, 184),
        Font = new Font("Segoe UI", 9f)
    };

    private static Button StepButton(string title, string detail, int x, int y, Color back)
    {
        var btn = new Button
        {
            Text = title + Environment.NewLine + detail,
            Location = new Point(x, y),
            Size = new Size(680, 58),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0),
            Font = new Font("Segoe UI Semibold", 11f),
            FlatAppearance = { BorderSize = 0 }
        };
        // WinForms multiline button uses \n; set Text properly
        btn.Text = title + "\n" + detail;
        btn.Font = new Font("Segoe UI", 9.5f);
        return btn;
    }

    private static Button SmallButton(string text, int x, int y, Color back) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(210, 36),
        FlatStyle = FlatStyle.Flat,
        BackColor = back,
        ForeColor = Color.White,
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
        Font = new Font("Segoe UI", 9f)
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
            _nextHint.ForeColor = Color.FromArgb(252, 165, 165);
        }
        else if (!hasRules)
        {
            _nextHint.Text = "Next: arrange your windows, then click step 1 to save them.";
            _nextHint.ForeColor = Color.FromArgb(103, 232, 249);
            Highlight(_btn1);
        }
        else if (!logon)
        {
            _nextHint.Text = "Next: click step 2 to test, then step 3 to restore at every sign-in.";
            _nextHint.ForeColor = Color.FromArgb(103, 232, 249);
            Highlight(_btn2);
        }
        else
        {
            _nextHint.Text = "You’re set. Re-run step 1 anytime after you rearrange windows.";
            _nextHint.ForeColor = Color.FromArgb(167, 243, 208);
            ClearHighlight();
        }

        var logonText = logon ? "sign-in restore ON" : "sign-in restore off";
        _statusLine.Text = disabled
            ? $"  Stopped  ·  {rules} saved window(s)  ·  {logonText}"
            : $"  {rules} window(s) saved  ·  {logonText}  ·  {DateTime.Now:t}";
        _statusLine.ForeColor = disabled ? Color.FromArgb(252, 165, 165) : Color.FromArgb(148, 163, 184);
    }

    private void Highlight(Button focus)
    {
        ClearHighlight();
        focus.FlatAppearance.BorderSize = 2;
        focus.FlatAppearance.BorderColor = Color.FromArgb(165, 243, 252);
    }

    private void ClearHighlight()
    {
        foreach (var b in new[] { _btn1, _btn2, _btn3 })
        {
            b.FlatAppearance.BorderSize = 0;
        }
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
            // Status line: Ready / Running = enabled; Disabled = off
            return Regex.IsMatch(output, @"Status:\s*(Ready|Running)", RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private const int MaxLogLines = 500;

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _log.AppendText($"[{stamp}] {line}{Environment.NewLine}");

        // Cap history so the log doesn't grow forever
        var lines = _log.Lines;
        if (lines.Length > MaxLogLines)
        {
            _log.Lines = lines.Skip(lines.Length - MaxLogLines).ToArray();
        }

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
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
        // Prefer multi-resolution icon (taskbar needs 16/24/32, not only 256)
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
