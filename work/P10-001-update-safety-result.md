# P10-001 result — update integrity and rollback safety

## Changed

- `GitHubUpdateService.cs`: checksum-required release discovery, SHA-256 verification, staged validation, touched-file backup, headless post-copy check, and failure restoration.
- `UpdateHealthCheck.cs` and `Program.cs`: non-GUI update health-check command.
- `SelfTest.cs`: deterministic checksum success/failure regression.
- `.github/workflows/release.yml`: staged health check plus checksum creation, local verification, and upload.
- `SWCouponManager.csproj`: version 1.4.6.
- `README.md` and decision/task routing documents: behavior, limits, evidence, and rollback contract.

## Not changed

Coupon sources/parsers, normalization, retry policy, account/Hive execution, result classification, persistence data, GUI controls, fixtures, existing validation records, and the installed distribution.

## Evidence

- TESTED: source restore/publish using .NET SDK 8.0.424; offline self-test exit 0; staged update health check exit 0; checksum mismatch rejected by self-test; `git diff --check`.
- NOT TESTED: network release workflow, actual GitHub update installation, forced post-copy rollback, GUI, live sources, or real accounts.
- INFERRED: rollback is bounded to package-touched paths and cannot overwrite `%LOCALAPPDATA%` state.

## Next recommendation

Use a disposable copy of a packaged 1.4.5 installation to run successful 1.4.6 replacement and injected post-copy failure/restore scenarios. Only after that evidence should the installed auto-update rollback be reported TESTED or a `v1.4.6` release be published.
