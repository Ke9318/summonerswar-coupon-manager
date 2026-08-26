# SWCouponManager Project Charter

## Product purpose

SWCouponManager exists to discover current Summoners War coupon codes, safely try new codes for the intended accounts, avoid missing valid coupons, and avoid repeatedly retrying codes already classified as completed or unusable.

The maintainable implementation is the source repository. A published `SWCouponManager-win-x64` folder is a generated distribution, not the place to implement product changes. Repository code is implementation truth; documents record intent, constraints, evidence, and decisions.

## Product invariants

- Prefer recall over aggressive filtering. A plausible false positive may be attempted once when safe; a known bad, expired, already-redeemed, or successful code is not retried unless policy explicitly changes.
- Source coverage is auditable. Do not silently remove or ignore a source because its markup is inconvenient.
- If an authoritative or established community source lists a code the program missed, open a coverage defect investigation. Do not infer that the code is invalid.
- Keep discovery, normalization/deduplication, attempt policy, account execution, result classification, UI, updater, and persistence separable. Avoid broad unrelated edits for a narrow defect.
- Operational values belong in configuration/data when practical; changing a source, seed, timeout, or policy value should not require unrelated engine edits.
- GitHub/self-update behavior must be explicit, testable, integrity-aware, and rollback-safe. Never claim it works unless the relevant path was tested.
- Keep account/Hive IDs and any credentials or secrets out of repository files, governance documents, fixtures, screenshots, and logs. Redact diagnostic evidence. User state remains separate from product code.
- Account automation fails closed when account identity or server selection is ambiguous.
- Prefer clear GUI/direct controls over prompt-driven operation.
- Minimize user manual work: use direct edits, patches, or scripts instead of asking the user to create, move, or rename files.

## Collaboration contract

User intent is a requirement, not an implementation command. First understand the outcome and code constraints; suggest a materially better approach when useful, without expanding scope without approval.

Use one disposition for proposed work:

- **APPROVE** — understood and safe inside current scope; proceed.
- **SUGGEST-CONTINUE** — a better in-scope approach exists; explain briefly and continue.
- **SUGGEST-DECISION** — a real product, safety, or architecture choice is required; stop for the choice.
- **BLOCK** — proceeding would be unsafe, destructive, unverifiable, or based on unresolved critical ambiguity.

Work continuously between checkpoints. Stop only for a real product/safety/architecture decision, destructive change, missing authority, or ambiguity that cannot be resolved from code and evidence.

## Task scale and evidence

- **Small:** narrow, reversible, low-risk change. A short task entry plus focused validation is enough.
- **Standard:** multi-file behavior or operational change. Record scope, risks, validation plan, and result.
- **Critical:** credentials/account execution, persistence migration, release/update, destructive action, or architecture boundary. Require rollback, explicit risks, and failure-path validation.

Validation labels are exact:

- **TESTED** — directly exercised with recorded evidence.
- **NOT TESTED** — not exercised; never report PASS.
- **INFERRED** — supported by inspection or indirect evidence only.

Discovery is not permission to fix. Record out-of-scope findings in `discoveries/` or `work/backlog.md`. Separate refactor/structure work from feature/bug work where practical. Back up consequentially edited user data or existing documents and state the rollback method.

## Definition of Done

A task is done only when scope and exclusions are recorded, implementation and docs agree, proportionate validation ran, every claim is labeled TESTED/NOT TESTED/INFERRED, relevant failure and rollback paths were considered, sensitive data is absent, discoveries are routed, and `HANDOFF.md` points to the next active work without becoming a diary.

Project lessons belong in `knowledge/`; durable choices belong in `decisions/`. Promote a lesson to shared/global knowledge only after reuse in another project or when clearly platform-wide. `C:\Users\Admin\Desktop\l;anis\AI_DEV_KNOWLEDGE` may be consulted when available, but this project must remain usable without it.
