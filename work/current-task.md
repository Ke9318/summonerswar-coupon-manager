# Current task — P15 redacted anomaly and repair gates

- Tier: Standard; any automatic code merge/release activation remains High and gated
- Disposition: APPROVE for evidence generation, deterministic policy, workflow definition, and local tests
- Status: Complete locally; all external activation remains gated

## Objective

Create privacy-safe anomaly evidence and deterministic repair eligibility gates. Low/Medium source/parser defects may produce a review artifact only after reproducible fixtures and offline gates; Critical account, persistence, updater, permission, and release changes must never be auto-approved.

## Preserve

All P10-P14 safety evidence, source attribution, recall, privacy, attempt isolation, rollback, and explicit activation boundaries.

## Validation plan

- Generate evidence containing correlation ID, source health/counts, payload hashes, parser/client version, and policy decision without raw payloads or account fields.
- Classify repair eligibility fail-closed: only source/parser diagnostics with fixtures may be suggested; account, attempt/history migration, updater, permissions, workflow activation, and release are blocked from automatic approval.
- Test deterministic JSON, privacy scan, repeated-evidence grouping, and critical-boundary rejection.
- Add review-only workflow scaffolding without issue creation, write permission, merge, push, release, or schedule activation.
- Re-run build/self-test/offline/live gates and document remaining activation blockers.

## Rollback

Evidence uses synthetic/redacted data only. Source remains recoverable against Git `8b64287`; no real installation or user state will be modified.

## Result

Completed locally. Evidence grouping/privacy and repair eligibility gates are TESTED; remote workflow/AI/merge/release paths are NOT TESTED and inactive. See [`P15-001-anomaly-repair-gates-result.md`](P15-001-anomaly-repair-gates-result.md).
