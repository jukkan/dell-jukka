# Changelog

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
