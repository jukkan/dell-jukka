# Development guide

Jukka’s Dell is a Windows-native C# / WPF utility. Read [AGENTS.md](../AGENTS.md) before changing the hardware-facing code. [VALIDATION.md](../VALIDATION.md) distinguishes tested behavior from open checks.

## Build and run

Use the Windows .NET 10 SDK. The WSL .NET runtime cannot execute this WPF application.

From Windows PowerShell at the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\Launch.cmd
```

The default publish is self-contained for Windows x64. `build.ps1 -FrameworkDependent` produces a smaller package that requires the Windows .NET 10 Desktop Runtime. NuGet restores System.Management.

Quit the UI before publishing. Closing its window only hides it to the tray; use Quit to release executable/DLL locks.

Source, launcher, scripts and documentation belong in Git. `artifacts/`, `bin/` and `obj/` are ignored. The README screenshot is a curated documentation asset under `docs/images/`, separate from transient captures.

## Architecture

- **Ordinary monitoring:** native Windows APIs collect CPU, memory, process and power data every four seconds while visible. Device/capacity readings use an isolated worker roughly every 30 seconds.
- **Dell access:** an on-demand elevated helper reads firmware attributes or performs one restricted setting write. Results return over a random, current-user-only named pipe. No permanent service or scheduled task is installed.
- **Write verification:** only four thermal profiles and four standard charging modes are writable. The helper checks firmware capability, serializes writes and verifies the result by reading it back. Custom mode and thresholds remain read-only.
- **Failure handling:** source failures and unknown readings remain explicit. Device and Dell observations carry separate timestamps. Slow reads have deadlines; a timed-out write has an uncertain outcome until a new read confirms it.
- **Activity/runtime:** up to 15 minutes of history stay in memory. Missing samples and monitoring pauses break traces. Estimated runtime uses remaining Wh and a time-weighted recent-minute discharge average, requiring at least 16 seconds of continuous valid battery readings.

The executable uses `asInvoker` and inherits the launching user's token. Start it normally from Explorer for standard-user operation. Dell settings require elevation for reads on the tested laptop; ordinary monitoring does not.

## Source map

All C# and XAML files below are under `src/JukkasDell/`.

| Area | Files | Responsibility |
| --- | --- | --- |
| Startup | `Program.cs` | Single-instance UI and restricted CLI/helper modes |
| Window | `MainWindow.xaml`, `MainWindow.xaml.cs` | Layout, polling, tray, controls and timestamped display |
| Theme | `DarkTheme.cs`, `AppIcon.cs` | Dark native caption, tray styling and app icon |
| Sessions | `MonitoringSession.cs` | Reject results from a previous hide/resume session |
| Activity | `ActivityHistory.cs` | History, events and smoothed runtime estimates |
| Verification | `SettingChange.cs` | Capability gates and write/read-back policy |
| Contracts | `Models.cs` | Snapshots, nullable readings and calculations |
| Dell provider | `DellProvider.cs` | Firmware/battery WMI reads and restricted thermal/charging writes |
| Elevation | `ElevatedChannel.cs` | Helper launch, restricted pipe protocol and deadlines |
| Isolation | `Workers.cs` | Slow read subprocess and timeout |
| Windows metrics | `Native.cs` | CPU, processes, memory, power scheme and battery flow |
| Export | `DiagnosticSummary.cs` | Timestamped summary without private hardware identifiers |
| Tests | `SelfTests.cs` | Calculation, validation, fake-provider and export checks |

## Command-line modes

Redirect output when invoking CLI modes from the Windows GUI executable.

| Mode | Purpose |
| --- | --- |
| `--power-snapshot` | Fast native battery-flow/remaining-capacity JSON, without elevation. |
| `--snapshot` | Read-only firmware/battery JSON, with source failures reported individually. |
| `--self-test` | Validation/calculation/export checks without hardware writes. |
| `--set-profile <profile>` | Administrator-only thermal write: Optimized, Quiet, Cool or UltraPerformance. |
| `--set-charging-mode <mode>` | Administrator-only charging write: Adaptive, Standard, Express or PrimAcUse. Custom and unknown values are rejected before WMI. |
| `--helper-test` | Elevated integration test of pipe reads and an idempotent Optimized operation, only if Optimized is already selected. |
| `--charge-helper-test` | Elevated integration test requesting the existing charging-mode value only. |

Internal pipe helpers are `--dell-read <nonce>`, `--dell-set <profile> <nonce>` and `--dell-charge <mode> <nonce>`. Nonces must be 32-character GUIDs. No arbitrary WMI attribute, command, credential or output path is accepted.

Setting-write exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Verified success |
| 20 | Elevation required |
| 21 | Unsupported or read-only setting |
| 22 | Provider rejected/unavailable |
| 23 | Read-back mismatch |
| 24 | Another writer is busy |
| 25 | Helper timeout; actual state needs checking |
| 64 | Invalid arguments |

## Testing

`./scripts/test.ps1` runs self-checks and read-only live smoke checks. It must not change firmware settings or sleep/reboot the device.

The optional `./scripts/test-charging.ps1 -AllowFirmwareWrite` test explicitly changes a charging mode and restores the original in a finally block, with independent confirmation. It is restricted to the original development machine and requires an elevated developer shell; it is not a portable test for other laptops. It is never called by the standard read-only test script.

Use fake-provider tests for rejected/mismatched writes rather than manufacturing firmware failures. Keep actual firmware tests explicit, preserve the original state, and record what was verified. Hands-on checks still include standard-user UAC cancellation, unplugged runtime, AC transitions, physical sleep/resume and longer-term resource use.

## Further work

- Validate unplugged runtime, transitions and overhead over longer daily-use sessions.
- Investigate supported CPU temperature, package power and fan readings as a bounded sensor experiment.
- Design custom charging-threshold dependency ordering and partial-failure handling before exposing writes.
- Consider keyboard brightness/timeouts only after write behavior is validated.

These are development possibilities, not implemented features. Do not weaken Windows protections to obtain sensor readings or replace the on-demand helper with recurring elevation prompts.

## References

- [Dell BIOS WMI whitepaper](https://dl.dell.com/manuals/common/dell-agentless-client-manageability.pdf)
- [Microsoft WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Windows battery state](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_battery_state)
- [Native dark title bars](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-windows-themes)
