using System.Diagnostics;
using System.Text.Json;

namespace WindowLayout.Gui;

/// <summary>
/// Small always-on-top overlay that polls apply-progress.json and offers Stop.
/// Launched as: WindowLayout.exe --progress [optionalAppDir]
/// </summary>
public sealed class ProgressForm : Form
{
    private const string MutexName = "Local\\WindowLayout.ProgressOverlay";
    private const int PollMs = 200;
    private const int PinRetryMs = 2500;
    private const int DoneHoldMs = 2000;

    private readonly string _appDir;
    private readonly string _progressPath;
    private readonly string _cancelPath;
    private readonly Label _title;
    private readonly Label _phase;
    private readonly Label _message;
    private readonly Label _counts;
    private readonly SoftButton _stop;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _pinTimer;
    private Mutex? _mutex;
    private bool _closing;
    private bool _doneScheduled;
    private DateTime? _doneAt;
    private IntPtr _lastPinnedHwnd = IntPtr.Zero;

    public ProgressForm(string appDir)
    {
        _appDir = appDir;
        _progressPath = Path.Combine(appDir, "apply-progress.json");
        _cancelPath = Path.Combine(appDir, "apply-cancel.flag");

        Text = "Window Layout — applying";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        AutoSize = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        // ClientSize (not Height) — FixedToolWindow chrome was eating the Stop button.
        ClientSize = new Size(400, 178);
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;
        Padding = new Padding(16);

        var theme = AppTheme.Dark;
        BackColor = theme.BgHeader;
        ForeColor = theme.TextPrimary;

        const int pad = 16;
        const int stopW = 108;
        const int stopH = 36;
        var contentW = ClientSize.Width - pad * 2;

        _title = new Label
        {
            Text = "Restoring window layout",
            Font = new Font("Segoe UI Semibold", 11f),
            AutoSize = false,
            Location = new Point(pad, 14),
            Size = new Size(contentW, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = theme.TextPrimary,
            BackColor = Color.Transparent
        };
        _phase = new Label
        {
            Text = "Starting…",
            AutoSize = false,
            Location = new Point(pad, 42),
            Size = new Size(contentW, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = theme.AccentHint,
            BackColor = Color.Transparent
        };
        _message = new Label
        {
            Text = "",
            AutoSize = false,
            Location = new Point(pad, 66),
            Size = new Size(contentW, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = theme.TextSoft,
            BackColor = Color.Transparent
        };
        _counts = new Label
        {
            Text = "",
            AutoSize = false,
            Location = new Point(pad, 94),
            Size = new Size(contentW - stopW - 12, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = theme.TextMuted,
            BackColor = Color.Transparent
        };
        _stop = new SoftButton
        {
            Text = "Stop",
            Size = new Size(stopW, stopH),
            Location = new Point(ClientSize.Width - pad - stopW, ClientSize.Height - pad - stopH),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            BackColor = theme.BtnDanger,
            ForeColor = theme.SoftButtonFg,
            CornerRadius = 8,
            Font = new Font("Segoe UI Semibold", 9.5f)
        };
        _stop.Click += (_, _) => RequestStop();

        Controls.AddRange([_title, _phase, _message, _counts, _stop]);

        PlaceNearBottomRight();

        _pollTimer = new System.Windows.Forms.Timer { Interval = PollMs };
        _pollTimer.Tick += (_, _) => PollProgress();
        _pinTimer = new System.Windows.Forms.Timer { Interval = PinRetryMs };
        _pinTimer.Tick += (_, _) => TryPinToAllDesktops();

        Load += (_, _) =>
        {
            if (!TryTakeMutex())
            {
                _closing = true;
                Close();
                return;
            }

            WriteHwndHint();
            TryPinToAllDesktops();
            _pollTimer.Start();
            _pinTimer.Start();
        };

        FormClosed += (_, _) =>
        {
            _pollTimer.Stop();
            _pinTimer.Stop();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        };
    }

    public static int Run(string[] args)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--progress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-progress", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && Directory.Exists(args[i + 1]))
                    appDir = Path.GetFullPath(args[i + 1]);
                break;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new ProgressForm(appDir));
        return 0;
    }

    private bool TryTakeMutex()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out var created);
            if (!created)
            {
                _mutex.Dispose();
                _mutex = null;
                return false;
            }
            return true;
        }
        catch
        {
            return true; // still show overlay if mutex fails
        }
    }

    private void PlaceNearBottomRight()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - Height - 24;
    }

    private void RequestStop()
    {
        try
        {
            File.WriteAllText(_cancelPath, $"cancel\n{DateTime.UtcNow:o}\n");
        }
        catch
        {
            // ignore
        }

        _stop.Enabled = false;
        _stop.Text = "Stopping…";
        _phase.Text = "Cancelling";
        _phase.ForeColor = AppTheme.Dark.AccentWarn;
        _message.Text = "Stop requested — apply will exit between steps.";
    }

    private void WriteHwndHint()
    {
        try
        {
            var hwnd = Handle;
            // Merge into existing progress if present so apply can Pin-Window
            ApplyProgressSnapshot? existing = null;
            if (File.Exists(_progressPath))
                existing = TryReadProgress();

            var doc = new ApplyProgressSnapshot
            {
                Phase = existing?.Phase ?? "starting",
                Current = existing?.Current ?? 0,
                Total = existing?.Total ?? 0,
                Message = existing?.Message ?? "Overlay ready",
                State = existing?.State ?? "running",
                Hwnd = hwnd.ToInt64(),
                Pid = Environment.ProcessId,
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };
            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(_progressPath, json);
        }
        catch
        {
            // ignore
        }
    }

    private void PollProgress()
    {
        if (_closing) return;

        if (_doneAt is not null)
        {
            if ((DateTime.UtcNow - _doneAt.Value).TotalMilliseconds >= DoneHoldMs)
            {
                _closing = true;
                Close();
            }
            return;
        }

        var snap = TryReadProgress();
        if (snap is null) return;

        if (snap.Hwnd is null || snap.Hwnd == 0)
            WriteHwndHint();

        var phase = string.IsNullOrWhiteSpace(snap.Phase) ? "…" : snap.Phase!;
        _phase.Text = char.ToUpperInvariant(phase[0]) + phase[1..];
        _message.Text = snap.Message ?? "";
        if (snap.Total > 0)
            _counts.Text = $"{snap.Current} / {snap.Total}";
        else if (snap.Current > 0)
            _counts.Text = $"Step {snap.Current}";
        else
            _counts.Text = "";

        var state = (snap.State ?? "running").ToLowerInvariant();
        if (state is "done" or "cancelled" or "error")
        {
            _stop.Enabled = false;
            if (state == "cancelled")
            {
                _phase.Text = "Stopped";
                _phase.ForeColor = AppTheme.Dark.AccentWarn;
                _message.Text = string.IsNullOrWhiteSpace(snap.Message) ? "Apply cancelled." : snap.Message!;
            }
            else if (state == "error")
            {
                _phase.Text = "Error";
                _phase.ForeColor = AppTheme.Dark.AccentWarn;
            }
            else
            {
                _phase.Text = "Done";
                _phase.ForeColor = AppTheme.Dark.AccentOk;
                if (string.IsNullOrWhiteSpace(_message.Text))
                    _message.Text = "Layout apply complete.";
            }

            if (!_doneScheduled)
            {
                _doneScheduled = true;
                _doneAt = DateTime.UtcNow;
            }
        }
    }

    private ApplyProgressSnapshot? TryReadProgress()
    {
        try
        {
            if (!File.Exists(_progressPath)) return null;
            var json = File.ReadAllText(_progressPath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<ApplyProgressSnapshot>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private void TryPinToAllDesktops()
    {
        try
        {
            var hwnd = Handle;
            if (hwnd == IntPtr.Zero) return;

            // Prefer VirtualDesktop Pin-Window (shows on every desktop)
            if (_lastPinnedHwnd == hwnd) return;

            var ps = FindPowerShell();
            if (ps is null) return;

            var escaped = hwnd.ToInt64().ToString();
            var psi = new ProcessStartInfo
            {
                FileName = ps,
                Arguments =
                    "-NoProfile -WindowStyle Hidden -Command \"" +
                    "try { Import-Module VirtualDesktop -DisableNameChecking -EA Stop; " +
                    "Pin-Window -Hwnd ([IntPtr]" + escaped + "); exit 0 } catch { exit 1 }\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return;
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return;
            }
            if (p.ExitCode == 0)
                _lastPinnedHwnd = hwnd;
        }
        catch
        {
            // Best-effort — overlay still works on the current desktop
        }
    }

    private static string? FindPowerShell()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    private sealed class ApplyProgressSnapshot
    {
        public string? Phase { get; set; }
        public int Current { get; set; }
        public int Total { get; set; }
        public string? Message { get; set; }
        public string? State { get; set; }
        public long? Hwnd { get; set; }
        public int? Pid { get; set; }
        public string? UpdatedUtc { get; set; }
    }

}
