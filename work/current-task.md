# Current task — P18 unattended activation repair

- Tier: Critical
- Disposition: APPROVE — user requested diagnosis and correction of watcher scheduling and legacy coupon filtering concerns
- Status: Active

## Objective

Make unattended discovery and local execution operationally real: recover missed cloud schedules without duplicate dispatch, register the installed app for Windows login start, honor `--background` as a hidden tray start, and prove legacy `SeenCodes` cannot suppress an untried account+code.

## Preserve

Account/Hive privacy, per-account terminal suppression, no automatic retry-all, trusted-manifest integrity, source completeness gates, attempt isolation, rollback, and explicit high-risk approval boundaries.

## Validation plan

- Prove `SeenCodes` is display-only and that only terminal/ambiguous/backoff state for the same account+code blocks automatic planning.
- Add a non-overlapping 25-minute stale-manifest recovery dispatch to the existing 15-minute monitoring heartbeat.
- Run the watcher on relevant main pushes while retaining the independent GitHub schedule.
- Test login-start registry value generation/removal with injected synthetic stores.
- Test background activation state persistence and hidden GUI lifecycle with synthetic data.
- Publish and verify v1.5.1 before enabling the installed app without reading real account state.

## Result

Pending.
