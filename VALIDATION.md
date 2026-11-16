# Validation — 0.1.0

Verified on JukkaXPS15, Dell XPS 15 9530, Windows 11 Business build 26200, November 10, 2026. These are observations from this machine, not compatibility promises.

## Passed

- Windows .NET 10 Release compilation: no errors or warnings. Self-contained Windows x64 publish succeeded; approximately 173 MiB unpacked including runtime.
- 19 self-checks: strict profile allowlist, rejection before WMI, missing/sentinel battery values, capacity ratio, CPU arithmetic/reset, null/zero JSON round-trip, diagnostic timestamps, pipe nonce validation and timeout reporting.
- Read-only live snapshot: XPS 15 9530, BIOS 1.33.0, Optimized, 67.454 / 84.290 Wh and 60 cycles.
- Real helper firmware write: Optimized → Quiet, independent WMI read-back verified; finally restored Optimized and independently verified it. No other BIOS setting changed.
- Current-user named-pipe helper: elevated read and idempotent Optimized operation returned fresh snapshots with success.
- Explicit privilege-boundary test: standard-user pipe server (`ServerElevated=false`) received a successful snapshot from elevated helper (`HelperElevated=true`), profile Optimized. No UAC automation used.
- Standard-user snapshot: ordinary device/battery reads succeeded; Dell namespace reads correctly reported administrator required. Windows power scheme reads now use native Power APIs, returning Balanced without elevation.
- Live UI inspected through Windows UI Automation and rendered window capture: CPU/memory/process list, battery capacity/cycles, separate Windows scheme/Dell profile, inactive custom thresholds and unavailable sensors. Standard-user UI subsequently observed with a successful on-demand Dell reading and no missing-source errors.
- Minimize hides to tray; second launch reopens existing window; one UI instance remains. Quit action exits cleanly.
- Short UI-process CPU sample, normalized over 20 logical processors: **0.424% visible over 16 seconds**, **0.034% hidden over 16 seconds**. Snapshot child CPU and provider-service CPU are not included. UI working set approximately 329 MiB at that observation. This is an initial sample on a busy system, not a long-term power benchmark.
- Monitoring paused while hidden and the app resumed sampling after reopening. Rendered preview is in ignored `artifacts/preview.png`.

## Still requires hands-on or longer testing

- Actual UAC rejection/cancellation and alternate administrator credentials. Successful standard-user UI reads and the cross-token pipe were observed; no automated secure-desktop interaction was attempted.
- Physical sleep/resume and AC/battery transition scenarios. Resume handling is implemented, but the laptop was not suspended for tests.
- Long-term resource/power behavior, absent/hanging WMI providers and interference from other Dell utilities. Workers have deadlines and helpers have watchdogs; failure/status paths are covered at the calculation/validation level, not every live provider fault.
- Charging-mode/threshold writes, GPU/SSD monitoring, CPU temperature/fan RPM and hardware warning history are outside this build.

## Reproduce

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

`--helper-test` is an optional integration mode for an already elevated developer terminal: it proceeds only when Optimized is already selected and requests the same value. CLI output should be redirected when launching the GUI executable from PowerShell.

The SDK's first-run initialization reported creating an ASP.NET development certificate during the first build. No certificate trust command was run; the app does not use HTTPS or that certificate.

## 0.1.1 — November 15, 2026

Verified on JukkaXPS15. This section supplements the historical 0.1.0 evidence above.

- Windows .NET SDK reported 10.0.404. Release compilation and self-contained publish succeeded without reported warnings/errors.
- **27 self-checks passed**, without firmware writes. Added coverage for rejecting results from a previous hide/reopen/resume session, unread/missing/last-observed Dell labels, and diagnostic export of failed workload refreshes and uncertain Dell observations. Generation tests exercise session invalidation; they do not substitute for physical sleep/resume testing.
- Read-only hardware smoke test reported XPS 15 9530, BIOS 1.33.0, Optimized, 67.454 / 84.290 Wh, 60 cycles. The CLI shell was elevated; the UI was tested separately as a standard user.
- Windows UI Automation confirmed 0.1.1 running as a standard user, explicit unread Dell state, and disabled profile controls before the privileged capability read. Refresh completed with separate ordinary-device, workload and Dell status.
- Minimize hid the window. After a five-second settling period, six successive one-second checks found no `--snapshot` worker. Second launch reopened one existing UI window, and monitoring resumed. This is a short lifecycle check, not a long-term power measurement.
- App-only rendered capture inspected: controls and the new Dell-observation label rendered without overlap. Temporary smoke/capture scripts and screenshot are in WSL `/tmp/jukkas-dell-ux`, not maintained project artifacts.
- Tray read/profile commands are implemented using the existing helper and busy/capability gates; **the new tray commands have not been exercised end to end through UAC**. No profile writes were performed in this session. Physical sleep/resume, UAC cancellation, AC transitions and long-term CPU/power behavior remain open.
- Source/docs replaced during this session were backed up under `artifacts/source-backups/20261115-215346`. No Git repository or remote was created.

The SDK first-run output again reported development-certificate creation. No trust command was run. A restricted-shell Windows interop failure was resolved by running the same Windows operation outside the sandbox; no WSL repair or system security setting change was performed.

## 0.1.2 — compact dark UI, November 15, 2026

- Final Release/self-contained build succeeded without reported warnings/errors. The existing 27 self-checks and read-only hardware smoke check passed during this UI revision; final adjustments affected theme rendering and layout. No firmware writes were requested. Hardware smoke test reported Optimized.
- Default window is **820 × 680 logical pixels**, minimum 720 × 580. Windows UI Automation verified the final default dashboard does not require vertical scrolling and its core controls are visible. At minimum size, scrolling reaches the details expander while the toolbar/footer remain accessible. The default size was restored after verification.
- DwmGetWindowAttribute confirmed the native dark-title-bar flag. App-only captures at default/minimum sizes were visually inspected; dark surfaces, text, CPU sparkline, memory bar and narrow scrollbar rendered correctly. The final default capture is saved at `artifacts/preview-dark.png`.
- The custom details expander expanded/collapsed through UI Automation; its content confirmed **App 0.1.2 / Standard user**. Refresh completed, and profile controls stayed disabled before a privileged capability read. Keyboard focus on Refresh rendered with the mint border.
- Selected profile styles and the dark tray renderer are implemented; this pass did not exercise profile switching/UAC or capture the tray menu. The existing manual tests for UAC cancellation, physical sleep/resume, AC transitions and longer-term behavior remain open.
- Prior modified source/README files were preserved under `artifacts/source-backups/20261115-220431`. Source remains outside the vault with no Git history/remote created. Temporary visual-check scripts and minimum-size screenshot are in WSL `/tmp/jukkas-dell-dark`.

## 0.2.0 — battery power, runtime, timeline and charging modes, November 15, 2026

Verified on JukkaXPS15 / Dell XPS 15 9530. This section supplements the earlier version history.

- Release compilation and Windows x64 self-contained publication succeeded without reported warnings/errors. **68 self-checks passed**, without hardware writes: strict charge-mode/helper validation; fake provider missing/read-only/unsupported/idempotent/rejected/mismatched read-back cases; native 32-byte battery ABI and rate sentinels/signs/zero; runtime arithmetic, finite inputs, warm-up, AC/missing capacity/stale data/gaps/pause recovery; 15-minute history retention; export of signed watts/events.
- Read-only live discovery: `PrimaryBattChargeCfg=Adaptive`, advertised values Adaptive/Standard/Express/PrimAcUse/Custom, ReadOnly=0. Only the first four are accepted as app write targets. Custom and thresholds were not written.
- Query-only native SystemBatteryState returned remaining capacity without elevation in the standard-user UI. An initial elevated probe while charging reported 874 mW and 65.140 Wh remaining. Later plugged-in samples reported the unknown-rate sentinel 0x80000000; simultaneous WMI DischargeRate was -2147483648, also unknown. The app correctly showed power unavailable. In the final standard-user activity capture, charging resumed and the app displayed **9.2 W into the battery**, with both CPU and battery-power traces visible. The unknown-rate interval is not evidence of actual discharge or a charger diagnosis, and battery flow is not total-system power.
- **Real charging integration passed** via the explicitly invoked `scripts/test-charging.ps1 -AllowFirmwareWrite`: current-mode pipe helper verification, Adaptive → Standard with independent snapshot confirmation, then restoration to Adaptive in finally with independent verification. Thermal profile remained Optimized; stored custom thresholds were not touched. This successful restore is a verified test result, not a requested permanent charging-policy change.
- Windows UI Automation verified **0.2.0 / Standard user**, all four charging controls present and disabled before privileged capability read, existing thermal gating, Refresh, custom expanders, and activity range/event rendering. Default **820 × 720** layout fits without vertical scrolling; at minimum 720 × 580, scrolling reaches details while toolbar/footer stay accessible. App-only captures inspected; final default/history previews saved as `artifacts/preview-power.png` and `artifacts/preview-activity.png`.
- The native battery source runs in the existing visible four-second workload sampler. No additional periodic WMI/elevation or persistent logging was introduced. Pure tests confirm pause/gap handling. The new timeline's physical sleep/resume and long-term resource behavior still need hands-on measurement; this session does not establish a power-efficiency improvement.
- **Remaining manual checks:** actual unplugged discharge and smoothed runtime compared with use; standard-user UAC consent/cancellation for charging controls (helper was exercised from an already elevated developer shell); AC transitions and physical sleep/resume. All four values are advertised, but only Adaptive/Standard were actually written during this test; Express/PrimAcUse remain capability-gated but not live-write-tested.
- Prior source/AGENTS/test files were preserved under `artifacts/source-backups/20261115-223621`. New files have no previous counterpart. No Git history/remote created. Temporary visual/probe scripts live under WSL `/tmp/jukkas-dell-power`.
