# P15-001 result — redacted anomaly and repair gates

## Outcome

Completed locally. Evidence generation and repair classification are deterministic and review-only; no AI change can auto-merge, push, activate a workflow, or release.

## Evidence

- TESTED: evidence contains correlation, versions, source counts/health, and full payload hashes without raw payload, coupon strings, account/Hive fields, or credentials.
- TESTED: identical payload/parser evidence maps to one equivalence group despite different correlation/time.
- TESTED: source-parser/source-diagnostic with a regression fixture yields `ReviewOnly`; missing fixture, non-parser feature, account/history/attempt/persistence/updater/permission/workflow/release/installed-app scope yields `Blocked`.
- TESTED: forced Rebuild `1.5.0`; packaged self-test; packaged update health check; offline regressions; `git diff --check`.
- NOT TESTED: remote anomaly workflow, external AI repair, pull-request review, push/merge, schedule, or release activation.

## Residual activation blockers

- Exact production PowerShell updater process-exit/restart path.
- Interactive two-process/tray/manual-vs-automatic GUI exercise.
- Real WebView/Hive automatic redemption, which must use a safe test account only with explicit approval.
- Remote workflow execution and Windows-client artifact delivery authentication/transport.

## Rollback

Source remains recoverable against Git `8b64287`; the completed roadmap should receive a local commit before any remote activation.
