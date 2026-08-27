# P17-002 result — approved two-account coupon probe

## Scope

With explicit user approval, submit the requested coupon once to each of the two eligible selected Hive accounts. Do not emit account identifiers or automatically repeat submissions.

## Safety change

- Added `--approved-code` so the approved live path rejects a run unless the exact requested code is present in the fresh trusted manifest.
- Added `--approved-account-count`, bounded to 1 or 2, so this approval could execute exactly two account+code work items and no more.
- Extended the explicit terminal-recovery path to honor the same 1-or-2 account bound without selecting the same account twice.
- Existing attempt isolation, persisted result handling, and redacted result-file contract remained in force.

## Result

- TESTED: Release build, full self-test, and `git diff --check` passed before the live probe.
- CORRECTED: the attached screenshot was initially misread as `THEB3STIMAGE`; that wrong manual candidate was removed after the prior ChatGPT monitoring thread established the intended spelling `THEB3STMAGE`.
- TESTED: watcher run `33035076658` published exactly one `THEB3STMAGE` candidate and no `THEB3STIMAGE` candidate.
- TESTED: the normal planner produced no pending work for `THEB3STMAGE`, demonstrating that both account+code identities were already terminal in application history without inspecting the state file.
- TESTED: explicit bounded terminal recovery for `THEB3STMAGE` planned 2, completed 2, failed 0, process exit 0.
- TESTED: Hive returned `invalid` for `THEB3STMAGE` on both account submissions.
- No account/Hive identifier, credential, or real state/log content was inspected or emitted.
- No automatic repeat or terminal override was used.
