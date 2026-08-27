# Current task — P16 interactive activation readiness

- Tier: Critical
- Disposition: APPROVE for disposable GUI/updater/workflow simulation; real account and remote activation remain gated
- Status: Complete locally; real-account and remote activation gated

## Objective

Directly exercise the remaining activation blockers without reading real user state: isolated GUI lifecycle, two-process exclusion, tray close/exit, manual/automatic operation ownership, and the exact production updater process-exit/copy/health/restart/rollback script.

## Preserve

All P10-P15 safety evidence, source attribution, privacy, attempt isolation, rollback, default-disabled automation, and explicit activation boundaries.

## Validation plan

- Add an explicit absolute `--data-dir` boundary and network-free disposable GUI smoke mode.
- Exercise first/second-process behavior and tray create/hide/explicit-exit against synthetic state.
- Extract the exact updater script builder without changing production semantics; run successful and injected-failure scenarios against a disposable packaged installation.
- Verify process exit/restart, health exit code, touched-file rollback, unrelated-file preservation, and external synthetic-state preservation.
- Rebuild/package/self-test/update-health/live completeness/diff check after repairs.
- Do not read real user state, use a real Hive ID, send redemption requests, replace the installed app, or activate remote workflows/releases until gates pass.

## Rollback

All GUI, package, logs, and state use unique temporary directories. Rollback points are Git `d8b5218` and disposable pre-test file hashes.

## Result

Completed locally. Disposable GUI/two-process and exact production updater success/rollback paths are TESTED. The approved live-one path was exercised but correctly sent no request because no pending trusted candidate exists; real Hive redemption remains NOT TESTED. Remote activation remains gated. See [`P16-001-interactive-activation-readiness-result.md`](P16-001-interactive-activation-readiness-result.md).
