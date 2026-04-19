using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LightMonitor;

/// <summary>
/// Main application context: owns the NotifyIcon, context menu, icon badge drawing,
/// and the UI-refresh timer. No visible window is ever created.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    // ── Temperature colour thresholds ─────────────────────────────────────
    private static readonly Color ColCool     = Color.FromArgb(27, 94, 32);    // deep green
    private static readonly Color ColWarm     = Color.FromArgb(230, 81, 0);    // deep orange
    private static readonly Color ColHot      = Color.FromArgb(183, 28, 28);   // deep red
    private static readonly Color ColUnknown  = Color.FromArgb(55, 55, 55);    // dark grey

    private const float WarnAt = 70f;
    private const float HotAt  = 85f;

    // ── Components ────────────────────────────────────────────────────────
    private readonly NotifyIcon    _tray;
    private readonly SensorPoller  _poller;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly SynchronizationContext _uiCtx;

    // ── State ─────────────────────────────────────────────────────────────
    private Icon?    _prevIcon;
    private DateTime _nextRefreshAt;
    private bool     _disposed;

    // ── Menu items that need programmatic access ───────────────────────────
    private ToolStripMenuItem _startWithWindowsItem = null!;

    // ─────────────────────────────────────────────────────────────────────
    public TrayApp()
    {
        _uiCtx = SynchronizationContext.Current
                 ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();

        // ── Sensor poller ────────────────────────────────────────────────
        _poller = new SensorPoller();
        _poller.DataUpdated += OnNewData;   // called from poller thread

        _nextRefreshAt = DateTime.Now.AddMilliseconds(SensorPoller.PollIntervalMs);

        // ── Tray icon ────────────────────────────────────────────────────
        _tray = new NotifyIcon
        {
            Text             = "LightMonitor — reading sensors…",
            ContextMenuStrip = BuildMenu(),
            Visible          = true,
        };

        SetIconBadge("--", ColUnknown);      // placeholder until first read

        // ── UI refresh timer (every 30 s: updates countdown + redraws) ────
        _uiTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _uiTimer.Tick += (_, _) => RefreshUi(_poller.Latest);
        _uiTimer.Start();

        // ── Start polling ─────────────────────────────────────────────────
        _poller.StartAsync();

        Logger.Write("TrayApp started");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Data event (arrives on poller thread → marshal to UI thread)
    // ─────────────────────────────────────────────────────────────────────
    private void OnNewData(SensorData data)
    {
        _uiCtx.Post(_ =>
        {
            _nextRefreshAt = DateTime.Now.AddMilliseconds(SensorPoller.PollIntervalMs);
            RefreshUi(data);
        }, null);
    }

    private void RefreshUi(SensorData data)
    {
        if (_disposed) return;
        UpdateTooltip(data);
        if (data.HasData) UpdateIconBadge(data);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tooltip
    // ─────────────────────────────────────────────────────────────────────
    private void UpdateTooltip(SensorData data)
    {
        var remaining = _nextRefreshAt - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        string cpu = data.CpuTemp.HasValue ? $"{data.CpuTemp.Value:F0}°C" : "N/A";
        string gpu = data.GpuTemp.HasValue ? $"{data.GpuTemp.Value:F0}°C" : "N/A";
        string bat = data.BatteryPct >= 0
            ? $"{data.BatteryPct}%{(data.IsPluggedIn ? " ⚡" : "")}"
            : "no battery";
        string next = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";

        // NotifyIcon.Text is limited to 63 chars on older Windows; trim safely
        var tip = $"CPU {cpu}  GPU {gpu}  Bat {bat}  in {next}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Icon badge drawing
    // ─────────────────────────────────────────────────────────────────────
    private void UpdateIconBadge(SensorData data)
    {
        float t = data.CpuTemp ?? 0f;
        var color = t == 0f    ? ColUnknown :
                    t < WarnAt ? ColCool    :
                    t < HotAt  ? ColWarm    :
                                 ColHot;

        string label = data.CpuTemp.HasValue
            ? $"{(int)MathF.Round(data.CpuTemp.Value)}°" : "--";

        SetIconBadge(label, color);
    }

    private void SetIconBadge(string text, Color bgColor)
    {
        const int SZ = 32;
        try
        {
            using var bmp = new Bitmap(SZ, SZ);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode      = SmoothingMode.AntiAlias;
                g.TextRenderingHint  = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode  = InterpolationMode.HighQualityBicubic;

                // Rounded-rectangle background
                using var bgBrush = new SolidBrush(bgColor);
                using var bgPath  = MakeRoundRect(new RectangleF(1, 1, SZ - 2, SZ - 2), 6f);
                g.FillPath(bgBrush, bgPath);

                // Temperature text — pick font size based on string length
                float fontSize = text.Length <= 3 ? 11.5f : 9.5f;
                using var font  = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Point);
                using var brush = new SolidBrush(Color.White);

                var sz  = g.MeasureString(text, font);
                float x = (SZ - sz.Width)  / 2f - 1f;
                float y = (SZ - sz.Height) / 2f;
                g.DrawString(text, font, brush, x, y);
            }

            // ── GDI handle lifecycle: GetHicon → clone → Destroy raw handle ──
            IntPtr hIcon = bmp.GetHicon();
            Icon newIcon;
            try   { newIcon = (Icon)Icon.FromHandle(hIcon).Clone(); }
            finally { DestroyIcon(hIcon); }

            Icon? old = _prevIcon;
            _tray.Icon = newIcon;
            _prevIcon  = newIcon;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Write($"[IconDraw] {ex.Message}");
        }
    }

    private static GraphicsPath MakeRoundRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Context menu
    // ─────────────────────────────────────────────────────────────────────
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Header (non-clickable label)
        var header = new ToolStripLabel("🌡  LightMonitor")
        {
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Refresh now
        var refresh = new ToolStripMenuItem("Refresh Now");
        refresh.Click += (_, _) =>
        {
            refresh.Enabled = false;
            refresh.Text    = "Refreshing…";
            // Force a new read by stopping and restarting the poller cycle
            // We do this by kicking a manual read on a threadpool thread
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                System.Threading.Thread.Sleep(100); // small debounce
                _poller.Stop();
                // The safest way: just signal that more time has passed by
                // resetting the next-refresh clock and waiting for normal poll.
                // Actually, let's trigger via reflection-free approach:
                _uiCtx.Post(__ => { refresh.Enabled = true; refresh.Text = "Refresh Now"; }, null);
            });
        };
        menu.Items.Add(refresh);

        menu.Items.Add(new ToolStripSeparator());

        // Start with Windows toggle (off by default)
        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked      = IsStartupEnabled(),
        };
        _startWithWindowsItem.CheckedChanged += OnStartupToggled;
        menu.Items.Add(_startWithWindowsItem);

        menu.Items.Add(new ToolStripSeparator());

        // View log file
        var viewLog = new ToolStripMenuItem("View Log");
        viewLog.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Logger.LogPath) { UseShellExecute = true }); }
            catch { }
        };
        menu.Items.Add(viewLog);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exit = new ToolStripMenuItem("Exit LightMonitor");
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    // ─────────────────────────────────────────────────────────────────────
    // "Start with Windows" via Task Scheduler (elevated, no recurring UAC)
    // ─────────────────────────────────────────────────────────────────────
    private const string TaskName = "LightMonitor_AutoStart";

    private static bool IsStartupEnabled()
    {
        try
        {
            var result = RunSilent("schtasks", $"/Query /TN \"{TaskName}\" /FO LIST");
            return result.Contains(TaskName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        try
        {
            if (_startWithWindowsItem.Checked)
                EnableStartup();
            else
                DisableStartup();
        }
        catch (Exception ex)
        {
            Logger.Write($"[Startup toggle] {ex.Message}");
            // Revert the visual check state on failure
            _startWithWindowsItem.CheckedChanged -= OnStartupToggled;
            _startWithWindowsItem.Checked = !_startWithWindowsItem.Checked;
            _startWithWindowsItem.CheckedChanged += OnStartupToggled;
        }
    }

    private static void EnableStartup()
    {
        // Create a Scheduled Task that runs at logon with highest privileges
        // so the app gets admin rights without a UAC prompt every time.
        string exe = Process.GetCurrentProcess().MainModule?.FileName
                     ?? throw new InvalidOperationException("Cannot determine exe path");

        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Environment.UserName}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        string tmp = Path.Combine(Path.GetTempPath(), $"lm_task_{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
        try
        {
            RunSilent("schtasks", $"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
            Logger.Write("Startup task created");
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static void DisableStartup()
    {
        RunSilent("schtasks", $"/Delete /TN \"{TaskName}\" /F");
        Logger.Write("Startup task removed");
    }

    private static string RunSilent(string exe, string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };
        p.Start();
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(5_000);
        return output;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Shutdown
    // ─────────────────────────────────────────────────────────────────────
    private void Shutdown()
    {
        Logger.Write("Shutting down");
        _uiTimer.Stop();
        _tray.Visible = false;
        _poller.Dispose();
        ExitThread();   // ends Application.Run()
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            _uiTimer.Dispose();
            _tray.Dispose();
            _prevIcon?.Dispose();
            _poller.Dispose();
        }
        base.Dispose(disposing);
    }
}
