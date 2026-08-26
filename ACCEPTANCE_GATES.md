# Acceptance Gates

## Source change gate

Required as applicable: clean build, self-test, offline captured-source regression, source-attribution checks, persistence/restart checks, and `git diff --check`. A PASS claim requires direct execution.

## Live completeness gate

Compare every required live source's explicit inventory with production extraction and preserve attribution. Non-empty output, two reads from one origin, or two parsers over one payload are not independent completeness/freshness proof. Network unverifiability is NOT TESTED and blocks release when live coverage is affected.

## Unattended execution gate

Demonstrate scheduler single-instance behavior, login/restart catch-up, account+code attempt identity, persist-before/around-execute ordering, terminal-result suppression, bounded temporary retries, ambiguous-result quarantine, cancellation, per-account isolation, and audit correlation. Manual retry-all must be unreachable from the automatic path.

## Release/update gate

Build → self-test → offline regression → relevant live completeness → staged health check → checksum/manifest verification → disposable successful update → injected failure rollback. Source push is not release activation; release is not installed-app confirmation.

## Reporting

Report outcome, changed scope, risk class, exact TESTED/NOT TESTED/INFERRED evidence, redacted live inventory attribution, residual risks, rollback point, and next automatic/user-approved action.
