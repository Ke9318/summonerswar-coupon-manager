# AI Working Model

## Evidence loop

Use one correlation ID across anomaly, source evidence, policy decision, diff, tests, review, activation, and rollback result.

1. Run cheap deterministic checks.
2. Detect an anomaly instead of repeatedly fetching identical evidence.
3. Capture redacted payload hash, source health, parser/build versions, and reproducible fixtures.
4. Search existing decisions, experience rules, tests, and similar regressions.
5. Classify cause and risk using `AUTONOMY_POLICY.md`.
6. Make the smallest isolated change.
7. Run the minimum sufficient gates, expanding only when a result differs or remains uncertain.
8. Review for recall loss, duplicate attempts, account isolation, secrets, rollback, and scope expansion.
9. Apply/activate only when the gate permits it; otherwise hold or roll back.

Treat repeated results from the same payload/origin/cache/parser path as one evidence-equivalence group. Do not count repetition as independent confidence. Escalate from deterministic checks to expensive AI investigation only when anomalies require it.

Compress repeated failures into this order: incident → recurring pattern → regression fixture → policy/gate. Do not hardcode historical coupon strings to make a test pass.
