# P11-001 progress — unattended foundation

## Outcome

Completed locally. Governance and the first local safety foundation are implemented; no cloud schedule, login-start agent, live redemption, release, or installed-app activation occurred.

## Implemented

- Canonical unattended Charter, risk/approval policy, AI evidence loop, acceptance gates, experience rules, architecture map, and accepted decision.
- Backward-compatible `Attempts` state with account+normalized-code identity, correlation ID, lifecycle, bounded temporary backoff, terminal suppression, and ambiguous quarantine.
- Trusted candidate manifest model with SHA-256, schema/client/time validation, normalized unique codes, attribution/evidence requirements, and cloud private-field rejection.
- Hive result classification separated from the GUI; unknown wording becomes `ambiguous` instead of an automatically retryable error.
- Existing remote 1.4.6 manual retry-all behavior retained, while tests prove the normal/unattended queue does not inherit it.
- Redemption coordination now persists queued and executing before adapter work, persists verifying at the final-submit boundary, and persists terminal, bounded temporary failure, or ambiguous quarantine afterward.
- Post-submit exceptions/cancellation fail closed as ambiguous; pre-submit failures/cancellation use bounded backoff. Temporary failure counts survive requeue.
- Automatic attempt correlation IDs reach redacted redemption audit lines; account names are no longer written to those progress lines.

## Evidence

- TESTED: remote main fast-forward and local-change reapplication; .NET build; offline self-test exit 0; persisted submission boundaries; terminal and ambiguous suppression; three-failure temporary backoff bound; pre/post-submit cancellation; post-submit crash recovery; per-account isolation; audit correlation propagation; manifest checksum/privacy/normalization; existing parser/source fixtures; manual retry-all characterization; `git diff --check`.
- NOT TESTED: NuGet vulnerability status because the audit service was unreachable (NU1900); live sources; real Hive/account execution; user-state migration in the installed app; background scheduler/tray; cloud watcher; GitHub Actions; update rollback; release/install activation.
- INFERRED: the added `Attempts` property is backward-compatible with missing JSON because `AppStorage.Load` initializes it, but real user-state migration remains NOT TESTED.

## Next implementation

Open P12 for scheduler/tray/login-start as a separate scoped task. Keep the installed app unchanged until disposable migration and redemption-contract tests pass.
