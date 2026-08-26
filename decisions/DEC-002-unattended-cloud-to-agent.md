# DEC-002 — Unattended cloud-to-agent operation

- Status: Accepted by user
- Date: 2026-08-26

## Decision

Routine operation becomes unattended: cloud collection continues while the PC is off; a trusted manifest carries candidates and evidence; a Windows login-start background agent catches up, redeems safely per account, persists results, updates itself through verified artifacts, and exposes GUI/tray controls for observation and intervention.

The initial cloud implementation should use scheduled GitHub Actions and repository/release artifacts before adding separate hosted infrastructure. AI repair is evidence-triggered and risk-gated, not a free-running agent with account access.

## Constraints

- Recall, especially SWC/emblem coverage, outranks false-positive reduction.
- Account data and redemption results remain local.
- The cloud side never receives Hive IDs or credentials.
- Automatic execution cannot use manual retry-all.
- High-risk boundaries remain user-approved under `AUTONOMY_POLICY.md`.

## First vertical slice

`scheduled source collection → signed/hashed candidate manifest → Windows manifest reader → attempt journal → per-account result → audit record`
