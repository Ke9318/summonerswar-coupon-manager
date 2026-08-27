# P17-002 result — approved two-account coupon probe

## Scope

With explicit user approval, submit only `THEB3STIMAGE` once to each of the two eligible selected Hive accounts. Do not emit account identifiers and do not retry.

## Safety change

- Added `--approved-code` so the approved live path rejects a run unless the exact requested code is present in the fresh trusted manifest.
- Added `--approved-account-count`, bounded to 1 or 2, so this approval could execute exactly two account+code work items and no more.
- Existing attempt isolation, persisted result handling, and redacted result-file contract remained in force.

## Result

- TESTED: Release build, full self-test, and `git diff --check` passed before the live probe.
- TESTED: fresh four-source publication gate included `THEB3STIMAGE` through GitHub Manual evidence.
- TESTED: planned 2, completed 2, failed 0, process exit 0.
- TESTED: Hive returned `invalid` for the approved code on both account submissions.
- No account/Hive identifier, credential, or real state/log content was inspected or emitted.
- No automatic repeat or terminal override was used.
