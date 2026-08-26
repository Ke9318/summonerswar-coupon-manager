# Current task — self-update integrity and rollback safety

- Tier: Critical
- Disposition: SUGGEST-CONTINUE
- Status: Complete locally; release not published

## Scope

Make the GitHub self-update path integrity-aware, validate the staged application before and after replacement, and restore touched installation files after failure. Keep coupon discovery, account execution, persistence semantics, GUI workflow, and user data unchanged.

## Implementation

- Release workflow emits and uploads a SHA-256 sidecar.
- Updater requires both ZIP and checksum assets and verifies the ZIP before extraction.
- `UpdateHealthCheck` validates required files, JSON readability, and assembly version.
- Apply script backs up only files it will touch, runs the new headless health check, and restores/removes only touched paths on failure.
- Project version advanced to 1.4.6. Decision recorded in `decisions/DEC-001-update-integrity-and-rollback.md`.

## Validation

- TESTED: .NET 8.0.424 restore/publish; offline `--self-test`; `--update-health-check --expected-version 1.4.6`; checksum acceptance and mismatch rejection; clean diff check.
- NOT TESTED: live GitHub release publication/download; a real installed update; forced post-copy rollback of a packaged installation; real GUI/account/coupon behavior.
- INFERRED: user state is unaffected because the updater operates on `AppContext.BaseDirectory` and temporary storage, while account state remains under `%LOCALAPPDATA%\SWCouponManager`.

## Rollback

Before commit, the original implementation remains recoverable from Git `HEAD`; no user state or published distribution was modified. Runtime rollback is limited to paths supplied by the staged package, preserving unrelated files.
