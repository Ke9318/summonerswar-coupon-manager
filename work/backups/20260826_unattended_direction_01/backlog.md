# Backlog

## Highest priority

### Critical — disposable packaged update/rollback integration test

The 1.4.6 source now verifies a release SHA-256, validates staged layout/version, backs up touched files, runs a headless post-copy health check, and restores touched paths on failure. Build and unit-level checks pass locally, but an actual packaged replacement and injected post-copy failure have not run. Exercise both in a disposable installation, confirm unrelated files and synthetic external user-state remain unchanged, and retain redacted evidence before publishing `v1.4.6`.

## Later

- Standard: move source definitions and operational thresholds toward explicit configuration where this reduces code edits without weakening per-source auditable parsers.
- Standard: add automated checks that logs and fixtures never contain Hive IDs or secret-like values; use synthetic identifiers.
- Small: keep versioned validation notes concise and avoid placing real observed account counts or other user-specific operational details in repository evidence.
