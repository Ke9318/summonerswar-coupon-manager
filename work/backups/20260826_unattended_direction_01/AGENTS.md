# AI Working Agreement

Read `PROJECT_CHARTER.md`, then `HANDOFF.md`, then `work/current-task.md` before changing anything. Inspect the relevant code; do not treat prose as proof of current behavior.

1. Classify the request Small, Standard, or Critical and state scope/exclusions.
2. Choose APPROVE, SUGGEST-CONTINUE, SUGGEST-DECISION, or BLOCK.
3. Continue autonomously within approved scope and checkpoint only for genuine decisions or risk.
4. Write a validation plan before consequential edits. Back up affected existing artifacts when rollback is not already provided by version control.
5. Keep behavior changes focused. Do not mix a discovery, refactor, and feature fix unless inseparable and recorded.
6. Never inspect, print, copy, or commit real `%LOCALAPPDATA%\SWCouponManager\state.json`, Hive IDs, credentials, or unredacted user logs. Use synthetic data and fixtures.
7. Preserve coupon recall, per-account history semantics, fail-closed account selection, source attribution/health reporting, and GUI-first operation unless a decision explicitly changes them.
8. Treat source removal, attempt/retry policy, result classification, account execution, persistence migration, and updater behavior as at least Standard; account identity, destructive migration, and release/update safety are Critical.
9. Run proportionate tests. Report only TESTED, NOT TESTED, and INFERRED; never turn inspection into PASS.
10. Put out-of-scope findings in `discoveries/` or `work/backlog.md`. Keep `HANDOFF.md` short and update it only as a router.

The source repository is maintainable truth. Published binary folders may receive a short pointer document, but product changes belong here.
