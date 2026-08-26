# P12-001 progress — local background-agent lifecycle

## Scope

Source-only lifecycle foundation. No live redemption, registry/Task Scheduler write, tray activation, installed-app replacement, release, or user-state migration.

## Implemented

- Single-instance scheduler start/stop ownership with non-overlapping execution.
- One immediate catch-up when overdue, followed by normal interval scheduling rather than burst replay.
- Pause/resume, cancellation, and bounded exponential failure backoff.
- Persistable additive background settings with automation disabled by default.
- Single-instance process lease connected to the GUI entry point; a second process informs the user and exits without disturbing the first.
- Pure tray close/explicit-exit policy ready for GUI ownership integration.
- Disabled-by-default advanced GUI toggle, tray show/hide/pause/explicit-exit ownership, and idempotent NotifyIcon disposal.
- Quoted login-start registration specification using `--background`; no registration writer or OS mutation.
- Trusted-manifest inbox with paired SHA-256 stable-read detection; missing, changing, corrupt, expired, or incompatible manifests fail closed before planning.
- Automatic planner carries source attribution, excludes ineligible/ambiguous accounts, and structurally exposes no retry-all override.
- Headless automatic cycle routes every item through the P11 persistence/submission coordinator, isolates item failures, propagates cancellation, and records terminal History.
- Disabled-by-default scheduler integration reads only the trusted inbox and shares the manual operation cancellation/working lease to prevent WebView overlap.

## Evidence

- TESTED: .NET SDK 8.0.424 Release build; offline self-test exit 0; scheduler duplicate-start rejection; non-overlap; catch-up; pause/resume; cancellation; 1/2/4-minute failure backoff; process lease exclusion; 10-cycle NotifyIcon create/pause/dispose; tray close policy; login-start absolute-path/quoting; manifest missing/stable/changing reads; automatic terminal/ambiguous/backoff filtering; fail-closed account eligibility; attribution preservation; item failure isolation; pre-attempt cancellation; existing offline source/attempt regressions; `git diff --check`.
- NOT TESTED: two real GUI processes; interactive tray show/hide/exit; real WebView automatic redemption; manual/automatic overlap in the live GUI; login-start registry/Task Scheduler registration; restart after login; installed app; live sources/Hive.
- INFERRED: new background state properties deserialize safely with false/null defaults; real user state was deliberately not inspected.

## Next

Proceed to P13 cloud watcher and manifest publication design. Keep OS login registration, installed activation, and real automatic redemption gated until disposable end-to-end failure-path tests exist.
