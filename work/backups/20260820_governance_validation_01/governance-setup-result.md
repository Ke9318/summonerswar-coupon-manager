# Governance setup and codebase audit result

## Changed

- Added `PROJECT_CHARTER.md` with project-specific product invariants, AI dispositions, checkpoint execution, task tiers, evidence vocabulary, Definition of Done, privacy, rollback, and knowledge/decision rules.
- Added `AGENTS.md` as the executable working agreement.
- Added short `HANDOFF.md`, current task, backlog, one lightweight task template, and minimal routing files for decisions, knowledge, and discoveries.
- Added a source/governance pointer to the published distribution; no executable or data file there was modified.

No pre-existing governance document was overwritten, so no document backup was necessary. The source clone and Git history provide rollback for additions.

## Inspected only

- Source discovery/coverage, reference inventory, trusted seed, account/history model, persistence, GUI/WebView2 redemption path, result/retry policy, logs, self-update, release workflow, self-tests, captured fixtures, and validation records.
- The published binary layout and embedded upstream repository/update/source URLs.
- No real local application state, Hive ID, credential, or user log content was read or copied.

## Validation claims

- TESTED: unique source repository identification; file inventory; governance content/pointers; clean documentation-only diff; existing offline self-test.
- NOT TESTED: current network source completeness; Hive redemption; ambiguous-account failure behavior in a real session; GUI; GitHub release job; installed update; rollback after failed update.
- INFERRED: coupon coverage controls are intentionally recall-oriented and auditable based on code, captured-source tests, and the release gate. Self-update is not rollback-safe based on direct code inspection.

## Recommended next task

Run a Critical, behavior-changing task to design and test update artifact integrity plus last-known-good rollback. Require an explicit decision on checksum/signature trust and backup retention before implementation. Do not combine it with coupon parser or account-execution changes.
