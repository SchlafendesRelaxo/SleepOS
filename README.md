# SleepOS

SleepOS is a Windows desktop application for system monitoring, maintenance, and opt-in performance management. It is built with WPF and .NET 10 and is designed for Windows 10 and Windows 11.

## Features

### Monitor and manage apps

- View system CPU and RAM readings and inspect eligible app processes, including their executable icon, CPU use, working set, PID, priority, and foreground/background state.
- Review hidden background apps. A high RAM number alone does not mean an app is unnecessary.
- Apply Windows EcoQoS or BelowNormal priority to selected apps. SleepOS records and restores the previous settings it changed.
- Request a working-set trim for selected background apps. This asks Windows to remove resident pages temporarily; Windows or the app may load them again. It does not reduce committed memory or guarantee a lasting RAM reduction.
- Stop selected processes only after confirmation. Unsaved work may be lost.

### Configure performance behavior

- Choose Balanced, Gaming, Extreme Performance, or Battery Saver policies.
- Opt in to per-app Memory Saver rules and set an inactivity delay for each app. For each rule, automatic working-set trimming can be enabled; with automatic Memory Saver on, SleepOS may request a trim at the selected mode's high-memory threshold and no more than once every 30 minutes per process.
- Enable automatic Memory Saver, game detection, and high-CPU analysis independently.
- Define game profiles for detected or user-added executables. SleepOS restores its temporary process policies when the game ends or SleepOS exits.
- Capture and save CPU, RAM, and background-process benchmark readings. Results are observed system deltas, not guaranteed improvements; SleepOS does not invent FPS gains.

### Maintain Windows

- Scan old files in the local Temp folder and review recommendations before cleanup.
- Find duplicate Desktop shortcuts and move selected shortcut files to the Recycle Bin.
- Use the optional Duplicate Tabs browser extension for Edge, Chrome, or Firefox. It is installed separately and asks before closing selected tabs.
- Open Windows Update, Storage, and Power settings without applying system changes automatically.

### Integrate with Windows

- Optionally start SleepOS at sign-in using the current user's Windows Run registration, with a configurable delay and minimized-to-tray startup.
- Use the notification-area icon to restore or exit SleepOS.
- Open Windows maintenance settings from within SleepOS. System-level changes remain under Windows control.

## Safety and limitations

- EcoQoS changes CPU and power scheduling; it does not free allocated RAM.
- Working-set trimming is temporary and can cause an app to reload data or stutter. It is an explicit action, not an automatic Memory Saver behavior.
- Memory Saver never suspends or terminates processes. When automation is enabled, it only targets opted-in, eligible apps that meet its inactivity and system-load rules.
- Windows-folder processes, Windows services, and SleepOS are protected from process actions. Automated EcoQoS and working-set trimming also avoid foreground apps and recognized active games.
- Process access can be denied by Windows. SleepOS reports failures instead of requiring administrator rights by default.
- Performance depends on the system and workload. No specific FPS or speed improvement is promised.

## Install

Download `SleepOS-Setup.exe` from the [GitHub Releases](https://github.com/SchlafendesRelaxo/SleepOS/releases) page and run it. Installing with the setup enables in-app updates. A portable build can be run separately, but must be installed with the setup before using the in-app updater.

## Build and run from source

Requirements:

- Windows 10 or Windows 11, x64
- .NET 10 SDK

From the repository root in PowerShell:

```powershell
dotnet restore SleepOS.slnx
dotnet build SleepOS.slnx --no-restore
dotnet run --project SleepOS.App\SleepOS.App.csproj
```

Run the test suite with:

```powershell
dotnet test SleepOS.Tests\SleepOS.Tests.csproj
```

## Repository layout

- `SleepOS.App` - WPF application and user interface
- `SleepOS.Core` - shared application models
- `SleepOS.System` - Windows process, startup, and system-management integrations
- `SleepOS.Monitoring` - system and process measurements
- `SleepOS.Games` - game detection and profiles
- `SleepOS.Settings` - settings persistence
- `SleepOS.Updater` - in-app update integration
- `SleepOS.Tests` - automated tests

For packaging and release instructions, see [RELEASING.md](./RELEASING.md).
