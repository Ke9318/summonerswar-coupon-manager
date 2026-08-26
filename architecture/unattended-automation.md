# Unattended automation architecture

## Components

1. **Cloud Watcher:** scheduled GitHub Action runs source collectors, stores redacted evidence/hashes, applies completeness gates, and produces a candidate-manifest artifact.
2. **Trusted Candidate Manifest:** versioned schema containing normalized code, source attribution, observed timestamps, evidence hashes, confidence/policy, minimum client version, expiry, and integrity metadata. It contains no account data.
3. **Windows Background Agent:** single-instance login-start process with tray status. It polls conditionally with interval+jitter+backoff, verifies the manifest, catches up, and queues only policy-approved account+code work.
4. **Attempt Journal:** durable lifecycle `QUEUED → EXECUTING → VERIFYING → TERMINAL` or `AMBIGUOUS`. It separates terminal History from retryable transport failures and closes restart/crash duplication gaps.
5. **Redemption Adapter:** owns Hive page interaction and result classification outside GUI callbacks. It fails closed on account/server ambiguity.
6. **Audit:** local redacted correlation records connect manifest evidence, attempt, result, retry decision, update, and rollback.
7. **Repair/Release:** deterministic gates precede AI repair; only policy-allowed changes activate automatically.

## Delivery phases

- **P11 foundation:** manifest schema/parser, attempt journal, redemption adapter boundary, automatic-path safety tests.
- **P12 local unattended agent:** scheduler, tray/background lifecycle, login start, conditional polling, catch-up, cancellation.
- **P13 cloud watcher:** scheduled collection, evidence bundle, completeness gate, trusted manifest publication.
- **P14 activation:** disposable end-to-end tests, verified update/rollback, staged release and installed-app confirmation.
- **P15 AI repair:** anomaly issue/evidence creation, sandbox repair, review/gate automation for low/medium risk only.
