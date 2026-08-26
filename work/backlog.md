# Backlog

## Highest priority

### Critical — production updater process-exit/restart integration test

The 1.5.0 package transaction now passes disposable successful replacement, health validation, unrelated-file/external-state preservation, and injected post-copy byte-for-byte rollback. The remaining gate is the exact production PowerShell process-exit/restart path with a disposable GUI process. Do not publish `v1.5.0` until that path and interactive tray/WebView cancellation are directly exercised.

## Later

- Standard: move source definitions and operational thresholds toward explicit configuration where this reduces code edits without weakening per-source auditable parsers.
- Standard: add automated checks that logs and fixtures never contain Hive IDs or secret-like values; use synthetic identifiers.
- Small: keep versioned validation notes concise and avoid placing real observed account counts or other user-specific operational details in repository evidence.
