# Autonomy Policy

## Risk classes

- **Low:** candidate/attribution data, fixtures preserving real structures, deterministic parser fixes, diagnostics, documentation, non-behavioral refactors. Codex may implement, test, commit, and apply when gates pass.
- **Medium:** source-union logic, retry/backoff, scheduling cadence, GUI/tray lifecycle, manifest interpretation, background execution. Require offline regression, relevant live/GUI checks, AI self-review, bounded rollback, and explicit evidence. Proceed automatically only when the existing policy clearly determines behavior.
- **High:** account identity/data, History or attempt migration, secrets/authentication, updater trust boundary, permission expansion, destructive deletion, mass retry, public release with irreversible effect. Require user approval before behavior-changing implementation or deployment unless the user has explicitly approved that exact milestone.

Risk follows impact, not diff size.

## Routine automation authority

Within an approved milestone Codex may inspect, edit, build, test, repair failed tests, update docs, version, create local commits, and update the disposable/local application after gates pass. It may not silently erase data, weaken duplicate protection, expose identities, expand credentials, or turn a manual recovery operation into an unattended path.

Remote push, tags/releases, scheduled cloud workflows, and installed-app replacement are distinct activation steps. They may proceed without another prompt only when the active task explicitly includes that activation, all required gates are TESTED, rollback is available, and no high-risk boundary changed. Otherwise prepare the artifacts and stop before activation.

## Operational fail-closed rules

- Ambiguous account identity, server, manifest integrity, minimum client version, or terminal result blocks that item—not the entire healthy queue.
- Manual `retry all` is excluded from unattended execution.
- Cancellation and bounded backoff are mandatory.
- User state and Hive identifiers never enter cloud artifacts, repository fixtures, AI prompts, or ordinary logs.
