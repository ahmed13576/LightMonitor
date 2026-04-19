# LightMonitor

A barebones Windows system-tray temperature monitor. Zero distractions — just your CPU and GPU temps at a glance, updating every 5 minutes.

![Tray badge showing 46° in green with tooltip: CPU 46°C GPU 44°C Bat 67% in 4:59](.github/preview.png)

---

## Features

- **Live badge icon** in the system tray — colour-coded at a glance
- **Tooltip** shows CPU temp, GPU temp, battery %, and countdown to next refresh
- **5-minute polling** — not every second; the process truly sleeps in between
- **No visible window** — lives entirely in the tray
- **"Start with Windows"** toggle in the right-click menu (off by default)
- Clean **install / uninstall** via PowerShell scripts

### Badge colours

| Colour | CPU temperature |
|--------|----------------|
| 🟢 Green | < 70 °C |
| 🟠 Orange | 70 – 84 °C |
| 🔴 Red | ≥ 85 °C |

---

## Requirements

- Windows 10 / 11 (64-bit)
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) — or install it via winget:
  ```powershell
  winget install Microsoft.DotNet.Runtime.9
  ```
- Administrator rights on first launch (needed for GPU sensor access)

---

## Install

1. Download `LightMonitor.exe` from [Releases](https://github.com/ahmed13576/LightMonitor/releases)
2. Run `install.ps1`:
   ```powershell
   .\install.ps1
   ```
   This copies the exe to `%LOCALAPPDATA%\LightMonitor\` and creates a Start Menu shortcut.
3. Launch it — you'll see a UAC prompt once to allow hardware sensor access.

> **"Start with Windows"** is off by default. Toggle it from the right-click tray menu.  
> When enabled, a Scheduled Task runs the app at login with elevated rights — no UAC prompt on boot.

---

## Uninstall

```powershell
.\uninstall.ps1
```

Kills the process, removes the startup task, deletes all files and the Start Menu shortcut. Nothing left behind.

---

## Build from source

```powershell
# 1. Install .NET 9 SDK if you don't have it
winget install Microsoft.DotNet.SDK.9

# 2. Clone and build
git clone https://github.com/ahmed13576/LightMonitor.git
cd LightMonitor
.\build.ps1
```

Output: `dist\LightMonitor.exe`

---

## How it works

| Component | Detail |
|-----------|--------|
| Language | C# / .NET 9 |
| Sensor library | [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| CPU sensor | `CPU Package` temp (Intel) / `Tctl/Tdie` (AMD) |
| GPU sensor | `GPU Core` temp (NVIDIA, AMD, Intel Arc) |
| Battery | Windows `PowerStatus` API |
| Idle CPU | 0% — uses `ManualResetEventSlim.Wait()` between polls |
| RAM | ~50–60 MB private bytes (includes .NET runtime) |

---

## Safety features

1. **Single-instance mutex** — launching a second copy exits immediately
2. **4-second read timeout** — if a sensor read hangs, it's abandoned and the last value is kept
3. **BelowNormal thread priority** — the poller yields CPU to everything else
4. **Global exception handler** — unhandled exceptions are logged, not shown to the user
5. **100 KB log rotation** — the log file never grows unbounded
6. **Graceful shutdown** — Exit cleans up all GDI handles and threads before terminating

---

## Antivirus note

LibreHardwareMonitor temporarily loads a kernel driver (`WinRing0x64.sys`) to read GPU sensor data. Some antivirus tools flag this as suspicious — it's a false positive. The driver is open-source and part of the [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) project.

If your AV quarantines the exe, add `%LOCALAPPDATA%\LightMonitor\` to its exclusions.

---

## License

MIT
