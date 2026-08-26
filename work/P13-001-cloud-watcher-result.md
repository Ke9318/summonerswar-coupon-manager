# P13-001 result — cloud watcher and trusted publication

## Outcome

Completed locally and with a one-shot live disposable manifest. The checked-in workflow is manual-dispatch only; no schedule, remote artifact publication, push, or release activation occurred.

## Evidence

- TESTED: Release build; offline self-test; four-required-source publication gate; deterministic candidate ordering; full payload SHA-256 attribution; privacy-safe schema; Windows inbox compatibility; staged pair replacement; injected second-move failure rollback; `git diff --check`.
- TESTED: one live elevated watcher run on 2026-08-27 produced 24 reference-confirmed candidates from SWGT, SW-Teams, SWQ, and GitHub Manual. Five production-only UI tokens (`ADDED`, `CLICK`, `NOTIFICATIONS`, `THANKS`, `TIONAL`) were detected in the first 29-item run, converted into a regression, and excluded from trusted publication without changing the local recall union.
- NOT TESTED: GitHub Actions execution, recurring schedule, remote artifact download by the Windows client, real account redemption, public release, or installed activation.
- INFERRED: the manual workflow will use the same CLI and gates on `windows-latest`; it has not run remotely.

## Rollback

Source remains recoverable against Git `8b64287`. Publication uses backups and restores the previous manifest/checksum pair on an injected replacement failure. Disposable live outputs remain outside the repository.
