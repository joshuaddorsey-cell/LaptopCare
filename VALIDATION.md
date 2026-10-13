# Validation - October 13, 2026

- Release executable compiled successfully with the Windows .NET Framework compiler.
- Twelve self-tests passed: fixed maintenance command allowlist; clean, repaired and unrecognized SFC output handling; isolated-registry startup round trip and collision protection; high-use and low-available-memory thresholds; live memory and process inventory; self-protection.
- UI smoke test rendered all five tabs and checked the app/startup table layouts. The overview was visually inspected from an actual window capture.
- Monitoring on this laptop: approximately 44 MB resident memory. Over a 20-second interval, 0.12 CPU seconds on eight logical processors (about 0.075% total CPU averaged over that short interval). Four isolated monitor samples after initialization took about 10-12 ms each. These are observations, not a guaranteed maximum.
- A read-only `sfc /?` probe confirmed that redirected SFC output on this laptop is UTF-16LE. The maintenance runner explicitly decodes that output before recording it and interpreting the conclusion.
- Desktop shortcut created, source opened in VS Code, app left open. Minimize/close routes to the tray; Exit in the tray stops it.
- No DISM/SFC repair, disk scan, real startup change, app termination, uninstall or file cleanup was performed to test this build. Actual administrator repair execution and its recovery from interruption remain unverified until a requested maintenance run. Tests use only isolated temporary test registry keys for startup logic.
- Source and desktop executable are local; monitoring does not send data to GitHub or another service. GitHub holds the private source repository and downloadable release executable.
