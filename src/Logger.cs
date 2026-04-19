using System;
using System.IO;

namespace LightMonitor;

/// <summary>
/// Thread-safe, size-capped file logger.
/// Writes to %LOCALAPPDATA%\LightMonitor\lightmonitor.log.
/// Rotates automatically when file exceeds 100 KB.
/// </summary>
internal static class Logger
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LightMonitor", "lightmonitor.log");

    private static readonly object _lock = new();
    private const long MaxBytes = 100 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                RotateIfNeeded();
            }
        }
        catch { /* never crash in the logger */ }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length <= MaxBytes) return;

            // Keep the last ~50 KB to avoid losing recent entries
            var content = File.ReadAllText(LogPath);
            var trimmed = content[^(int)(MaxBytes / 2)..];
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
            File.WriteAllText(LogPath,
                $"[-- log rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} --]{Environment.NewLine}{trimmed}");
        }
        catch { }
    }
}
