# Falco CLI

English | [简体中文](./README.zh-CN.md)

A system optimization and cleanup toolkit for Windows 10/11 — the command-line edition is open source, and the GUI edition is free (closed-source freeware). Both are described below.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](./LICENSE)

## Editions & Availability

| Edition | Form | License | Availability |
|---------|------|---------|--------------|
| **Falco CLI** (this repo) | Command-line tool | GPL-3.0, free & open source | `git clone` and run |
| **Falco GUI** (desktop app) | GUI: live status dashboard, one-click boost, deep clean, software management, disk analysis, tray HUD, hourly World Art Gallery | Free to use (closed-source freeware) | [Download the installer from Releases](../../releases) (`gui-v` tag prefix) |

The GUI edition is **free to use and free to redistribute as the original, unmodified installer**, but its source code is not published and reverse engineering is not permitted. This repository hosts the free, open-source CLI edition; GUI installers are also published under [Releases](../../releases) with the `gui-v` tag prefix. Feedback and bug reports go to this repo's Issues.

> ⚠️ **Note on installing the GUI edition (transitional)**: the installer is not code-signed yet. If Windows SmartScreen shows "Windows protected your PC" when you run the installer, click "More info" → "Run anyway"; the UAC prompt mentioning an "unknown publisher" is expected — click "Yes" to continue installing.

## Features

- **status** — health score (0–100) plus a CPU / memory / disk / temperature / uptime snapshot; `-Json` for machine-readable output
- **tweaks** — inspect, back up, apply and revert six system tweaks (Delivery Optimization / Background apps / Visual effects / Active hours / SysMain / Windows Search); every write is read-back verified and rolled back automatically on failure
- **clean** — scan and clean temp files / Windows Update cache / thumbnail cache / Recycle Bin
- **purge** — scan for build artifacts (node_modules / target / build, etc.); automatically skips anything active within 7 days, containing secrets, or inside nested repositories
- **large** — large-file scan

Temperature data comes from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (optional dependency, `lib/` folder); when it is missing, the tool automatically falls back to the ACPI thermal zone.

## Usage

```powershell
# System status & health score
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status -Json

# Tweaks
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks list
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply safe        # the four low-risk tweaks
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply AH-001 -AHStart 8 -AHEnd 22
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks revert <Id|all>

# Cleanup
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean scan
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean run -Items temp,thumbs -Yes

# Build artifacts & large files
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 purge D:\code -Days 7
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 large C:\ -MinMB 500 -Top 20
```

### Permissions

`status` / `tweaks list` / `clean scan` / `purge` / `large` are read-only or need no elevation; `tweaks apply / revert` and the Windows Update cache part of `clean run` require **administrator rights**. All changes are backed up automatically to `C:\ProgramData\Falco\Backups` before they are applied; the restore script is `Restore-Falco.ps1`.

## Project Structure

```
Falco/
├── Falco-CLI.ps1                   # CLI tool (self-contained, single file)
└── lib/LibreHardwareMonitor/       # Temperature / hardware sensor library (optional, auto-fallback when missing)
```

## License

This project is open source under the [GPL-3.0](./LICENSE) license.

The third-party component [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (v0.9.6) under `lib/LibreHardwareMonitor/` is licensed under the **Mozilla Public License 2.0 (MPL-2.0)** and stays a separate, optional sensor provider: those files retain their original license and notices (see `lib/LibreHardwareMonitor/NOTICE.txt`) and are not affected by this project's GPL-3.0 terms.

## Disclaimer

This tool modifies system registry settings and service configurations. Although every change is backed up and reversible, it is recommended that you:

1. Manually create a system restore point before first use
2. Keep your own backups of important data
3. Restart the system after restoring defaults
