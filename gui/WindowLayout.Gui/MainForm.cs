using System.Diagnostics;
using System.Text;
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

    private string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private string RulesPath => Path.Combine(AppDir, "window-layout.rules.json");
    private string DisableFlag => Path.Combine(AppDir, "DISABLE-LAYOUT");

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

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(28, 18, 28, 12)
        };
        var title = new Label
        {
            Text = "Window Layout",
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Location = new Point(28, 16),
            ForeColor = Color.White
        };
        var subtitle = new Label
        {
            Text = "Save where your windows live, then restore them — including at sign-in.",
            AutoSize = true,
            Location = new Point(30, 52),
            ForeColor = Color.FromArgb(148, 163, 184)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 16, 28, 16)
        };

        _nextHint.AutoSize = false;
        _nextHint.Location = new Point(28, 8);
        _nextHint.Size = new Size(680, 36);
        _nextHint.Font = new Font("Segoe UI Semibold", 11f);
        _nextHint.ForeColor = Color.FromArgb(103, 232, 249);
        _nextHint.Text = "Loading…";

        _statusLine.AutoSize = false;
        _statusLine.Location = new Point(28, 44);
        _statusLine.Size = new Size(680, 22);
        _statusLine.ForeColor = Color.FromArgb(148, 163, 184);
        _statusLine.Text = "";

        var stepsLabel = SectionLabel("Setup (do once, or whenever you rearrange)", 28, 78);

        _btn1 = StepButton(
            "1  Save current layout",
            "Arrange your windows first, then click. Remembers apps, desktops, and positions.",
            28, 108, Color.FromArgb(8, 145, 178));
        _btn2 = StepButton(
            "2  Test restore now",
            "Moves windows back to the saved layout. Try this before turning on logon.",
            28, 178, Color.FromArgb(5, 150, 105));
        _btn3 = StepButton(
            "3  Turn on at sign-in",
            "Runs the restore automatically after you log into Windows.",
            28, 248, Color.FromArgb(37, 99, 235));

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
            Location = new Point(28, 322),
            LinkColor = Color.FromArgb(148, 163, 184),
            ActiveLinkColor = Color.FromArgb(103, 232, 249),
            VisitedLinkColor = Color.FromArgb(148, 163, 184)
        };

        _advancedPanel.Location = new Point(28, 348);
        _advancedPanel.Size = new Size(680, 52);
        _advancedPanel.Visible = false;

        var btnOff = SmallButton("Turn off sign-in restore", 0, 0, Color.FromArgb(71, 85, 105));
        var btnList = SmallButton("List open windows", 230, 0, Color.FromArgb(71, 85, 105));
        var btnFolder = SmallButton("Open files folder", 460, 0, Color.FromArgb(71, 85, 105));
        var btnKill = SmallButton("Emergency stop", 0, 0, Color.FromArgb(185, 28, 28));
        var btnClear = SmallButton("Clear emergency stop", 230, 0, Color.FromArgb(71, 85, 105));
        var btnRepair = SmallButton("Repair module", 460, 0, Color.FromArgb(71, 85, 105));

        // two rows of advanced
        _advancedPanel.Height = 96;
        btnKill.Location = new Point(0, 48);
        btnClear.Location = new Point(230, 48);
        btnRepair.Location = new Point(460, 48);

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

        _advancedPanel.Controls.AddRange([btnOff, btnList, btnFolder, btnKill, btnClear, btnRepair]);

        var logLabel = SectionLabel("Activity", 28, 360);
        // reposition log label dynamically via layout helper — use fixed with advanced collapsed default
        void PlaceLogArea()
        {
            var top = _advancedOpen ? 456 : 360;
            logLabel.Location = new Point(28, top);
            _log.Location = new Point(28, top + 28);
            _log.Size = new Size(ClientSize.Width - 56, Math.Max(120, ClientSize.Height - top - 56));
            moreToggle.Location = new Point(28, 322);
            _advancedPanel.Location = new Point(28, 348);
        }

        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Both;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(15, 23, 42);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = new Font("Consolas", 9f);
        _log.WordWrap = false;
        _log.BorderStyle = BorderStyle.FixedSingle;

        moreToggle.LinkClicked += (_, _) =>
        {
            _advancedOpen = !_advancedOpen;
            _advancedPanel.Visible = _advancedOpen;
            moreToggle.Text = _advancedOpen ? "More options ▾" : "More options ▸";
            PlaceLogArea();
        };

        body.Controls.AddRange([
            _nextHint, _statusLine, stepsLabel,
            _btn1, _btn2, _btn3,
            moreToggle, _advancedPanel,
            logLabel, _log
        ]);

        Controls.Add(body);
        Controls.Add(header);

        Resize += (_, _) => PlaceLogArea();
        Shown += (_, _) =>
        {
            PlaceLogArea();
            RefreshUiState();
            AppendLog("Arrange your windows, then follow steps 1 → 2 → 3.");
        };
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
            ? $"Stopped  ·  {rules} saved window(s)  ·  {logonText}"
            : $"{rules} window(s) saved  ·  {logonText}  ·  {DateTime.Now:t}";
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

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _log.AppendText($"[{stamp}] {line}{Environment.NewLine}");
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

            var pwsh = FindPwsh();
            if (pwsh is null)
            {
                AppendLog("PowerShell 7 (pwsh) not found. Install from https://aka.ms/powershell");
                MessageBox.Show(this,
                    "PowerShell 7 (pwsh) is required but was not found.\n\nInstall it from https://aka.ms/powershell",
                    "Window Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            AppendLog($"Running {scriptName}…");

            var args = new StringBuilder();
            args.Append("-NoProfile -ExecutionPolicy Bypass -File \"").Append(script).Append('"');
            foreach (var a in extraArgs)
                args.Append(' ').Append(a);

            var psi = new ProcessStartInfo
            {
                FileName = pwsh,
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

    private static string? FindPwsh()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

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
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (line is not null && File.Exists(line)) return line;
        }
        catch { /* ignore */ }

        return null;
    }
}
