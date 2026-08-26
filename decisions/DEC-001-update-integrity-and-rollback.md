# DEC-001 — Release checksum and touched-file rollback

- Status: Accepted
- Date: 2026-08-20
- Validation: mixed; see below

## Context

The updater previously trusted a GitHub Release ZIP after checking only that it contained `SWCouponManager.exe`, then overwrote the installation in place. A corrupt/mismatched artifact, incomplete layout, or failed new process could leave a broken installation without automatic recovery.

## Decision

- Publish a SHA-256 sidecar with every release ZIP. Do not offer an automatic update unless both assets exist.
- Verify the ZIP hash before extraction, then require the executable, application assembly, dependency/runtime JSON, WebView2 assemblies, native loader, and expected assembly version.
- Before copying, back up every existing destination file that the staged package will touch.
- After copying, run `--update-health-check --expected-version <version>` without opening the GUI.
- If copy or health check fails, restore backed-up files and remove only newly created files from the staged package, then restart the previous executable.
- Never include `%LOCALAPPDATA%\SWCouponManager` user state in update backup or replacement.

## Consequences

Existing releases without a checksum remain manually downloadable but are not offered through the automatic updater. SHA-256 protects transport/artifact consistency, not compromise of the repository or GitHub release publisher. A future independent signing requirement would supersede this decision.

## Validation

- TESTED: source compilation; checksum accept/reject self-test; staged layout/version health check; existing offline regression suite.
- NOT TESTED: a real installed update from GitHub; forced post-copy failure and automatic restoration in a packaged installation; GitHub Actions release publication.
- INFERRED: touched-file rollback avoids deleting unrelated installation files because it restores/removes only paths present in the staged package.
