# LaptopCare

A lightweight, local Windows desktop dashboard for memory pressure and Windows maintenance. Built in C# with Windows Forms on the Windows-provided .NET Framework 4.x. No browser, cloud account, paid service, additional SDK or background service is required.

## Use

Run `bin/LaptopCare.exe` after building, or use the LaptopCare desktop shortcut created on this computer. Minimize or close the window to monitor in the system tray. Double-click its tray icon to reopen; choose **Exit** from its tray menu to stop. Monitoring is every five seconds while visible and every fifteen seconds in the tray. It does not start automatically with Windows.

- **Overview:** physical RAM use, available memory, CPU activity, commit usage, recent RAM samples and system-drive free space. Memory pressure thresholds are advisory: high at 90% use or less than 512 MiB available; elevated at 80% use or less than 1 GiB available. These do not measure actual paging or establish the cause of a slowdown.
- **Memory & apps:** up to 80 largest readable processes, resident working set and growth since a rolling baseline of at most ten minutes. Working sets include shared pages; do not sum them as physical RAM usage. Selected windowed apps can receive a graceful close request after confirmation. No force-kill button; the app, Windows components, editors, Codex/ChatGPT and maintenance processes are protected. Browsers may prompt to save work.
- **Startup:** disable and restore current-user `Run` registry entries. Original command and registry type are preserved under `HKCU/Software/LaptopCare/DisabledStartup`. Changes apply to the next sign-in. Startup folders, scheduled tasks, services, machine-wide entries and Windows-managed StartupApproved states are not changed; Windows Settings is linked for these other entries.
- **Windows repair:** check with `DISM /Online /Cleanup-Image /ScanHealth` then `sfc /verifyonly`; repair with `DISM /Online /Cleanup-Image /RestoreHealth` then `sfc /scannow`; system-drive online scan with `chkdsk /scan`. Each requires confirmation and Windows UAC. Commands are fixed, with no arbitrary command entry. A named mutex and process checks prevent duplicate app repairs and avoid known running DISM/SFC/CHKDSK processes. A separate administrator worker continues if the dashboard exits. No restart or offline disk repair is scheduled.
- **Tools & history:** storage cleanup review, installed apps, Windows Update, reliability history and an exportable current health report.

Repair logs and action history live in `%LOCALAPPDATA%/LaptopCare`. The repair state records final exit codes and the SFC conclusion. Unrecognized/localized SFC output is explicitly unverified; read the full log. A completed command is not proof that every performance problem is fixed. Interrupted repairs retain their log and must be acknowledged before a new scan. This application cannot prevent another program from independently launching a repair at the same time.

## Build and verify

From Windows PowerShell in this folder:

```powershell
.\build.ps1
Start-Process .\bin\LaptopCare.exe -ArgumentList '--self-test' -Wait
Start-Process .\bin\LaptopCare.exe -ArgumentList '--ui-test' -Wait
```

VS Code: **Terminal > Run Build Task**. The build uses the compiler supplied with Windows' .NET Framework. The resulting x64 executable is portable; no installer is required. It is unsigned.

Tests write `self-test.txt` and `ui-test.txt` under the local data folder. They exercise command allowlisting, output interpretation, memory-pressure thresholds, live Windows readings, self-protection, startup disable/restore and collision handling in isolated temporary test registry keys, and all five UI tabs. They do not launch Windows repairs, close other apps, uninstall software or change actual startup entries. `--benchmark` measures four monitor samples and writes `benchmark.txt` locally.

## Scope and privacy

No automatic app termination, RAM flushing, registry cleaner, automatic deletion, driver replacement or telemetry upload. Cleanup and uninstall links open Windows' own review screens. Temperature, disk SMART, per-process CPU, page-fault rates and persistent crash/slowdown timelines are not implemented in this first version. Protected processes can be omitted when Windows denies access. Local exports contain process names; review before sharing. Actual UAC repair execution must be validated when a repair is wanted; it was deliberately not invoked merely to test this application.

Microsoft references: [System-file repair order](https://support.microsoft.com/en-au/windows/experience/backup-recovery/using-system-file-checker-in-windows), [Windows image maintenance](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image?view=windows-11), [Memory API](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex).
