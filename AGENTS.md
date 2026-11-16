# Jukka’s Dell

Native Windows personal utility. Read README.md, docs/DEVELOPMENT.md and VALIDATION.md before changes. README is the public-facing product overview; the development guide contains the source map, CLI reference and proposed next steps. VALIDATION distinguishes verified behavior from open tests.

- Source: `src/JukkasDell`; build: Windows PowerShell `./scripts/build.ps1`; read-only validation: `./scripts/test.ps1`. Optional charging integration test: `./scripts/test-charging.ps1 -AllowFirmwareWrite` (records/restores the original mode).
- Run the Windows SDK, not the WSL runtime, for WPF execution. Publish output lives under ignored `artifacts/app`.
- Quit the running UI before publishing; closing its window only hides it to the tray. Source/docs are versioned in the private `jukkan/dell-jukka` GitHub repository. Inspect Git status before editing; keep builds, local backups and transient evidence ignored.
- Verify hostname before live hardware tests. The development target is JukkaXPS15, XPS 15 9530.
- Firmware writes are limited to `ThermalManagement` (four allowlisted profiles) and `PrimaryBattChargeCfg` (Adaptive, Standard, Express, PrimAcUse). Custom mode and charge thresholds are read-only. Do not introduce a generic BIOS writer, credentials, security changes, automatic profile enforcement, or elevated service/task.
- Keep the UI unprivileged. Dell settings require elevation even for reads on this machine. On-demand helpers return timestamped data over a current-user-only named pipe. Do not replace this with periodic UAC prompts.
- Preserve unknown/null states, per-source errors and separate Dell/basic-data timestamps. Firmware status fields do not establish hardware health.
- Profile round-trip tests must restore Optimized. Read-only tests must remain read-only. Never reboot/sleep the device as an automated test without the user's explicit instruction.
- Pause routine polling while hidden; evaluate CPU/power overhead before adding sensor collectors. Do not collect/export serials, service tags, credentials, command lines or user paths.
- Keep executable output and transient evidence out of versioned source. Do not commit the adjacent Obsidian vault or create a Git repository at vault level.

- Battery watts/remaining Wh use the query-only native SystemBatteryState API; positive watts mean charge, negative mean discharge. They are battery flow, not whole-laptop or adapter power.
- Runtime requires at least 16 seconds of continuous battery discharge and uses a time-weighted recent-minute average. Invalidate across pauses, missing samples, gaps and AC transitions. Activity history is bounded to 15 minutes in memory and marks pauses; never connect chart lines across missing data.
- Charging tests must preserve and verify the original charging mode and thermal profile. Never silently leave a test mode selected or edit custom thresholds. Standard read-only tests must not call the opt-in charging test.
