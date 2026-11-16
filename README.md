# Jukka’s Dell

A small local Windows tray app for the Dell XPS 15 9530. Version 0.2.0.

Private source repository: [jukkan/dell-jukka](https://github.com/jukkan/dell-jukka).

## Run

Double-click `Launch.cmd`, or open `artifacts\app\JukkasDell.exe`.

- Click **Read Dell settings…** to load the current profile and enable switching. This laptop requires administrator access even to read Dell settings. Windows may ask for approval.
- Right-click the tray icon to read Dell settings or switch profiles. Its tooltip and checked profile show the **last observation**, not a continuously verified state.
- Switch between Optimized, Quiet, Cool and Ultra Performance. A short-lived elevated helper writes only the thermal profile, verifies it and returns refreshed Dell readings.
- Compact dark dashboard with a native dark title bar, mint profile selection and a matching tray menu. Battery and busy processes sit side by side; extra device details expand on demand.
- See CPU load/history, physical memory and the five busiest accessible processes.
- See battery percentage, live battery watts, remaining Wh, estimated unplugged runtime, reported cycles and estimated capacity retention. Runtime warms up for at least 16 seconds on battery and follows the recent-minute discharge average.
- Expand **Charging controls** to select Adaptive, Standard, Express or Primarily AC after reading Dell settings. Changes use the on-demand helper and verified read-back; custom thresholds remain read-only.
- Expand **Recent activity** for up to 15 minutes of CPU/battery-power traces and AC, observed-profile/mode and monitoring-pause markers. History stays in memory; monitoring pauses while hidden.
- **Refresh** samples workload and ordinary device/battery readings; **Read Dell settings…** separately refreshes privileged settings. The profile card shows when Dell settings were last observed and asks for another read after resume or an uncertain helper result.
- Copy a diagnostic summary with process names and observation timestamps. Serial numbers, service tags, account names, command lines and executable paths are not collected for export.
- Close or minimize the window to leave it in the tray. Monitoring pauses while hidden. Double-click the tray icon to reopen; use Quit to exit.

The app has no cloud account, telemetry, automatic startup, background service or autonomous profile switching. The framework includes the required runtime in the default published build. Keep all files in the `artifacts\app` folder together.

## Build and verify

Windows .NET 10 SDK required for development; NuGet restores System.Management. Run from Windows PowerShell:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

`build.ps1 -FrameworkDependent` makes a smaller development package that requires the Windows .NET 10 Desktop Runtime. Build output is ignored by Git. Source, launcher, scripts and documentation are versioned in the private GitHub repository; binaries and transient evidence are excluded.

**Quit the running app before rebuilding.** Closing its window only hides it; use the window's Quit button or the tray menu. Otherwise publishing can fail because the executable or DLLs are locked. After a build, reopen `Launch.cmd`.

From WSL on this laptop, run the Windows build tools:

```bash
powershell.exe -NoProfile -File 'C:\Dev\jukkan\dell-jukka\scripts\build.ps1'
powershell.exe -NoProfile -File 'C:\Dev\jukkan\dell-jukka\scripts\test.ps1'
```

The source folder is `/mnt/c/Dev/jukkan/dell-jukka` in WSL. Windows WPF execution and live WMI tests require Windows; the WSL .NET runtime cannot run this app. Build scripts use the source folder relative to their own location, so no other working directory is required.

CLI modes (run with redirected output to read JSON/text from the Windows GUI executable):

- `--power-snapshot`: fast native battery-flow/remaining-capacity JSON, without elevation.
- `--snapshot`: read-only firmware/battery JSON; source failures are reported individually.
- `--self-test`: validation/calculation/export checks, without hardware writes.
- `--helper-test`: run from an already elevated developer terminal; tests pipe-based reads and an idempotent Optimized operation, only if Optimized is already selected.
- `--dell-read <nonce>` / `--dell-set <profile> <nonce>`: internal elevated pipe helpers; nonce must be a 32-character GUID. No generic BIOS operation is exposed.
- `--set-charging-mode <mode>`: administrator-only charge-mode write (Adaptive, Standard, Express, PrimAcUse); exit 0 means verified. Custom/unknown values are rejected before WMI. Internal `--dell-charge <mode> <nonce>` uses the same restricted operation over the elevated pipe.
- `--charge-helper-test`: optional elevated integration check of the current charging mode through the helper; requests the existing value only.
- `--set-profile Optimized`: administrator-only, allowlisted thermal write; exit 0 means read-back verified. Other profiles: Quiet, Cool, UltraPerformance.

Exit codes for profile writes: 20 elevation required, 21 unsupported/read-only, 22 provider rejected/unavailable, 23 read-back mismatch, 24 another writer busy, 25 helper timeout (state uncertain; read again), 64 invalid arguments. The command does not accept arbitrary WMI attributes or BIOS credentials.

## First-build limits

- Four charging modes are writable when firmware advertises them: Adaptive, Standard, Express and PrimAcUse. **Custom mode and start/stop limits remain read-only.** Stored limits are inactive unless firmware reports Custom. Switching away from an existing Custom configuration does not erase its stored thresholds.
- CPU temperature and fan RPM are unavailable through the interfaces checked. GPU/SSD sensors, hardware warning history and low-level sensor libraries are deferred.
- WMI schemas and access vary by device, firmware and user permissions. Missing values remain unavailable; failures do not imply a healthy device.
- Capacity retention is a firmware estimate, not a battery diagnosis or remaining-lifespan prediction.
- CPU is sampled time across all logical processors, not Task Manager’s frequency-adjusted CPU utility; figures may differ. Protected processes may be inaccessible. WSL appears as one Windows workload.
- Ordinary readings refresh every 30 seconds while visible. Dell settings use a separately timestamped, on-demand elevated reading. Refresh updates ordinary readings; **Read Dell settings…** checks changes made by other apps. Every thermal change also refreshes the Dell reading. No periodic UAC prompts.
- Failed refreshes retain prior data with its original timestamp. A successful partial reading marks unavailable sources; failures never masquerade as fresh measurements.
- Firmware updates or another app may change settings. Read Dell settings to observe those changes. The app does not enforce a preferred profile; profile switching rechecks firmware capability even if the displayed cached profile looks current.
- The package is a local unsigned development build. A normal-user UAC consent/cancellation interaction, physical sleep/resume and longer-term behavior still need hands-on verification; see `VALIDATION.md` for measured results.

## Internals

WPF UI → Windows APIs for workload and power state; isolated child process → System.Management for slow Dell/battery reads. A read worker has a 20-second parent deadline. The short-lived elevated helper has a 25-second self-watchdog; timed-out writes have an uncertain outcome until read-back. Elevated reads/write results return over a random-named, current-user-only pipe; the helper accepts no output path or arbitrary commands. No overlapping UI writes; a named mutex serializes helper writes. UI workers are asynchronous and single-flight. The only write targets are `ThermalManagement` and four non-Custom `PrimaryBattChargeCfg` modes, each with a fixed allowlist, firmware-advertised capability check and post-write read-back. A shared helper mutex serializes both setting types.

The executable uses `asInvoker`; it inherits the launching user's token. Start it normally from Explorer rather than an elevated terminal for standard-user operation. UAC uses the same trusted local executable; no elevated service or scheduled task is installed.

Source references: [Dell BIOS WMI whitepaper](https://dl.dell.com/manuals/common/dell-agentless-client-manageability.pdf), [Microsoft WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [Microsoft performance counters and direct APIs](https://learn.microsoft.com/en-us/windows/win32/perfctrs/about-performance-counters).

## Continue development here

Read [AGENTS.md](AGENTS.md) for implementation boundaries and [VALIDATION.md](VALIDATION.md) for what has actually been tested. This folder contains the source, build scripts and essential handoff; the Obsidian planning note is supporting context, not a build dependency. The first build was requested and delivered on November 10, 2026. The following next steps are agent proposals, not commitments by Jukka.

| Area | Source file | Responsibility |
| --- | --- | --- |
| Startup and CLI | `Program.cs` | UI single instance; restricted helper/test modes |
| Window | `MainWindow.xaml`, `MainWindow.xaml.cs` | Layout, polling lifecycle, tray, cached Dell state and separate timestamps |
| Theme | `DarkTheme.cs` | Window-local dark caption and matching tray menus |
| Monitoring sessions | `MonitoringSession.cs` | Reject pre-hide/resume results; format last-observed Dell state |
| Recent activity/runtime | `ActivityHistory.cs` | Bounded history, pause/AC events and time-weighted drain estimate |
| Setting verification | `SettingChange.cs` | Capability gates and write/read-back policy; fake-provider failure checks |
| Data contracts | `Models.cs` | Snapshots, nullable readings, validation and calculations |
| Dell/battery WMI | `DellProvider.cs` | Selected reads and the single allowlisted thermal writer |
| Privileged operations | `ElevatedChannel.cs` | On-demand UAC, current-user-only pipe, helper status and deadline |
| Slow read isolation | `Workers.cs` | Read-only child-process collection and parent timeout |
| Workload and power | `Native.cs` | Windows APIs, CPU/process sampling, memory and power scheme |
| Export | `DiagnosticSummary.cs` | Timestamped summary without private hardware identifiers |
| Verification | `SelfTests.cs`, `scripts/test.ps1` | Pure checks and read-only live smoke test |

All source filenames above are under `src/JukkasDell`, except the script.

Suggested order for the next session:

1. **Use the current build first.** Capture concrete UX issues. Check cancelled UAC, sleep/resume and AC transitions manually. Keep basic monitoring unprivileged; do not replace the on-demand helper with recurring elevation requests.
2. **Measure resource use over longer periods.** The first measurement covers only the UI process for short windows. Include snapshot workers and provider overhead before claiming a power or efficiency improvement.
3. **Use the new battery controls and timeline.** Four standard modes are implemented and verified through the restricted writer. Optional `scripts/test-charging.ps1 -AllowFirmwareWrite` explicitly round-trips a charging mode and restores the original; it is never run by read-only `test.ps1`. Custom thresholds require a separately designed dependency/partial-failure implementation before exposing writes. Test runtime after manually unplugging; do not disconnect power or sleep the device automatically.
4. **Treat sensors as a separate experiment.** GPU/SSD readings were available during earlier diagnosis but are not implemented in the app. CPU temperature and fan RPM need further investigation, potentially LibreHardwareMonitor; support and privilege/driver requirements are unproven. Do not make the usable app depend on this experiment or weaken Windows protections to obtain readings.

Keyboard brightness/timeouts are another optional convenience; no write behavior has been validated. Fleet support, arbitrary BIOS editing, automatic profile rules, updates/installers and low-level fan control are outside the current scope.

Temporary test scripts, snapshots and the UI preview are under ignored `artifacts/`. They are evidence from this session, not a maintained test suite. Reproduce routine checks with `scripts/test.ps1`; no automated test should sleep/reboot the laptop or silently change charging settings. Git now preserves source/docs in the private repository. Build artifacts can be regenerated; ignored local evidence and source-backup folders are not uploaded.

## 0.1.1 — daily-use improvements (November 15, 2026)

- Tray menu offers Dell read and the four supported profile operations through the existing on-demand helper. Controls share the window's busy/capability checks. A tray action opens the window so progress, cancellation and errors remain visible.
- Dell profile observations have a date/time beside the controls. Cached settings are explicitly marked for rechecking after resume or an uncertain helper result. A failed Dell read no longer displays a success message just because the helper returned status zero.
- Refresh updates workload as well as device readings. Results begun before hide/reopen or resume are discarded; a completed workload sample cannot start a device collector while hidden. Existing collectors remain single-flight.
- Copied summaries retain workload-refresh failures and uncertain Dell-state warnings.

Physical sleep/resume and secure-desktop UAC cancellation remain manual acceptance checks. See VALIDATION.md for this session's verified results.

## 0.1.2 — compact dark UI (November 15, 2026)

Charcoal surfaces, mint accents, smaller controls and typography, a compact CPU sparkline, and side-by-side battery/process panels. The default window is 820 × 680 logical pixels (previously 1000 × 850); minimum size is 720 × 580 with scrolling for smaller windows or expanded diagnostics. Profile descriptions, observation timestamps, missing-data states and full diagnostic exports remain available. Dark styling covers selected/disabled/focused controls, tooltips, progress bars, scrollbars, device-details expander, native title bar and tray menu.

The native Windows frame retains resizing, caption buttons and Snap. Title-bar styling uses window-local [DWM attributes](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-windows-themes); it does not change the Windows theme. Hardware controls and polling behavior are unchanged in this UI revision.

## 0.2.0 — battery power, runtime, activity and charging controls

- Native query-only [SystemBatteryState](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_battery_state) adds battery watts and remaining Wh to the ordinary four-second visible workload sampler. Positive watts mean charging; negative mean discharge. These figures describe battery flow, not adapter or total laptop power. Unknown/sentinel or contradictory values remain unavailable.
- Estimated runtime = remaining Wh / smoothed recent discharge watts. The time-weighted average uses up to the latest minute and requires at least 16 seconds of continuous valid discharge. Missing readings, long gaps, monitoring pauses and AC transitions require new valid samples. No runtime is shown while plugged in. This is an estimate based on recent use, not a promise about future workload.
- Recent activity retains up to 15 minutes in memory with separate CPU and signed battery-watt scales. History survives hiding/reopening; missing samples and pauses break the lines. Observations/verified operations are timestamped alongside AC transitions. No persistent background logging is enabled.
- Charging controls are inside the battery card. Four supported modes share the thermal control's single-operation/elevation lifecycle; firmware capability is checked again in the helper. A rejected write or mismatched read-back remains a failure and triggers a new-state check. Custom mode/threshold writes are not accepted.
- Default window is 820 × 720 logical pixels to accommodate the additional status and activity expander. Minimum size remains 720 × 580; expanded panels scroll. Extra capacities remain in diagnostics and charging details.

Source-preservation and existing hands-on UAC/suspend/resume limitations still apply. See VALIDATION.md for this build's test evidence. Sensor integration is a later experiment; CPU temperature/fan RPM are not added in this version.
