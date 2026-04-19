using LibreHardwareMonitor.Hardware;
using System;
using System.Threading;
using WinForms = System.Windows.Forms;

namespace LightMonitor;

/// <summary>Snapshot of all sensor readings at one point in time.</summary>
public sealed class SensorData
{
    public float?   CpuTemp       { get; set; }
    public float?   GpuTemp       { get; set; }
    public string   GpuName       { get; set; } = string.Empty;
    public int      BatteryPct    { get; set; } = -1;   // -1 = no battery / unknown
    public bool     IsCharging    { get; set; }
    public bool     IsPluggedIn   { get; set; }
    public DateTime ReadAt        { get; set; } = DateTime.MinValue;

    public bool HasData => ReadAt != DateTime.MinValue;
}

/// <summary>
/// Runs a background thread that wakes every 5 minutes, reads sensor data from
/// LibreHardwareMonitor, and publishes the result via DataUpdated.
///
/// Safety guarantees:
///   - Thread priority is BelowNormal  → yields CPU to everything else
///   - Each hardware read is time-boxed to 4 s  → can't hang the app
///   - All exceptions are caught and logged; stale data is kept on failure
///   - Hardware is re-tried on next poll if init failed at startup
/// </summary>
public sealed class SensorPoller : IDisposable
{
    public const int PollIntervalMs = 5 * 60 * 1000;   // 5 minutes
    private const int ReadTimeoutMs = 4_000;             // max per read

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _dataLock = new();

    private Computer?   _hw;
    private SensorData  _latest = new();

    public SensorData Latest
    {
        get { lock (_dataLock) { return _latest; } }
        private set { lock (_dataLock) { _latest = value; } }
    }

    /// <summary>Raised on the poller thread after each successful read.</summary>
    public event Action<SensorData>? DataUpdated;

    public SensorPoller()
    {
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name         = "LightMonitor-Poller",
            Priority     = ThreadPriority.BelowNormal,
        };
    }

    // ── Public API ───────────────────────────────────────────────────────

    public void StartAsync()
    {
        InitHardware();
        _thread.Start();
    }

    public void Stop() => _stop.Set();

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(8_000);
        try { _hw?.Close(); } catch { }
        _stop.Dispose();
    }

    // ── Polling loop ─────────────────────────────────────────────────────

    private void PollLoop()
    {
        SafeRead();                         // immediate read on startup
        while (!_stop.Wait(PollIntervalMs)) // OS-level sleep; 0% CPU idle
            SafeRead();
    }

    /// <summary>Runs the hardware read on a throwaway thread; abandons it after timeout.</summary>
    private void SafeRead()
    {
        SensorData? result = null;
        using var done = new ManualResetEventSlim(false);

        var reader = new Thread(() =>
        {
            try   { result = ReadHardware(); }
            catch (Exception ex) { Logger.Write($"[ReadHardware] {ex.Message}"); }
            finally { done.Set(); }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "LightMonitor-Reader" };

        reader.Start();

        // ── Safety Guard 3: per-read timeout ────────────────────────────
        if (!done.Wait(ReadTimeoutMs))
        {
            Logger.Write("Read timed out — keeping previous data");
            return;                         // keep old Latest; reader thread cleans itself up
        }

        if (result != null)
        {
            Latest = result;
            try { DataUpdated?.Invoke(result); }
            catch (Exception ex) { Logger.Write($"[DataUpdated event] {ex.Message}"); }
        }
    }

    // ── Hardware access ─────────────────────────────────────────────────

    private void InitHardware()
    {
        try
        {
            _hw = new Computer
            {
                IsCpuEnabled         = true,
                IsGpuEnabled         = true,
                IsBatteryEnabled     = true,
                IsMotherboardEnabled = false,   // not needed, reduces init time
                IsControllerEnabled  = false,
                IsMemoryEnabled      = false,
                IsNetworkEnabled     = false,
                IsStorageEnabled     = false,
            };
            _hw.Open();
            Logger.Write("LibreHardwareMonitor initialised OK");
        }
        catch (Exception ex)
        {
            Logger.Write($"LHM init failed: {ex.Message}. Will retry next poll.");
            _hw = null;
        }
    }

    private SensorData ReadHardware()
    {
        // Re-try init if it failed at startup (e.g. transient driver error)
        if (_hw == null) InitHardware();

        var data = new SensorData { ReadAt = DateTime.Now };

        if (_hw != null)
        {
            foreach (var hw in _hw.Hardware)
            {
                try
                {
                    hw.Update();
                    ExtractSensors(hw, data);
                    foreach (var sub in hw.SubHardware)
                    {
                        sub.Update();
                        ExtractSensors(sub, data);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write($"[HW:{hw.Name}] {ex.Message}");
                }
            }
        }

        // Battery — WinForms PowerStatus needs no extra privileges
        try
        {
            var ps = WinForms.SystemInformation.PowerStatus;
            data.BatteryPct  = ps.BatteryLifePercent is >= 0f and <= 1f
                                ? (int)MathF.Round(ps.BatteryLifePercent * 100f)
                                : -1;
            data.IsCharging  = ps.BatteryChargeStatus.HasFlag(WinForms.BatteryChargeStatus.Charging);
            data.IsPluggedIn = ps.PowerLineStatus == WinForms.PowerLineStatus.Online;
        }
        catch (Exception ex) { Logger.Write($"[Battery] {ex.Message}"); }

        return data;
    }

    private static void ExtractSensors(IHardware hw, SensorData data)
    {
        bool isCpu = hw.HardwareType == HardwareType.Cpu;
        bool isGpu = hw.HardwareType is
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

        if (!isCpu && !isGpu) return;

        foreach (var s in hw.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
            float v = s.Value.Value;

            if (isCpu)
            {
                // Intel → "CPU Package"  |  AMD → "CPU Package" or "Core (Tctl/Tdie)"
                bool preferred = s.Name == "CPU Package"
                              || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                              || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);

                if (preferred || data.CpuTemp == null)
                    data.CpuTemp = v;
            }
            else // GPU
            {
                if (string.IsNullOrEmpty(data.GpuName))
                    data.GpuName = hw.Name;

                // "GPU Core" is the primary die temperature on NVIDIA and AMD
                if (s.Name == "GPU Core")
                    data.GpuTemp = v;
                else if (data.GpuTemp == null)   // fallback to first available GPU sensor
                    data.GpuTemp = v;
            }
        }
    }
}
