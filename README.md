# SleepOS

SleepOS is a Windows desktop client for system monitoring, safe maintenance, and opt-in performance management.

## Performance features

- **Performance policies:** Balanced, Gaming, Extreme Performance, and Battery Saver tune SleepOS polling and define thresholds for explicitly managed apps. These policies do not overclock hardware or promise specific FPS gains.
- **Memory Saver:** Add apps from **Games & processes → Background apps**, set an inactivity delay per executable, and enable automation in **Settings**. SleepOS uses Windows process efficiency mode (EcoQoS) to reduce CPU/power scheduling; it cannot force-release allocated RAM and does not suspend or terminate managed apps. Only eligible, visible, non-foreground, non-game applications are considered by automation.
- **Process manager:** Shows measured CPU, working-set RAM, PID, priority, foreground state, and executable icon. Users can apply EcoQoS or set a selected app to BelowNormal priority, then restore the prior process settings. The confirmed **Trim working set** action can temporarily remove resident pages from selected background apps; Windows or the app may load them again, so it does not promise a lasting RAM reduction.
- **Gaming automation:** When enabled, SleepOS recognizes supported running games and temporarily applies the active game's selected policy to opted-in background apps. Add additional executables from **Background apps** to create per-game profiles; policies are restored after the game ends.
- **Benchmark:** Captures real CPU, RAM, and background-process readings and keeps up to 20 snapshots in local settings. Deltas are observed system changes, not proof that SleepOS caused an improvement; no FPS estimates are generated.
- **Windows sign-in and tray:** Optional per-user startup is registered in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; startup delay and minimized-to-tray behavior are configurable. No administrator access is required.

Automatic Memory Saver, gaming detection, and high-CPU analysis can be enabled independently in Settings. Process actions are limited to eligible non-Windows applications; no services or protected Windows processes are modified.
