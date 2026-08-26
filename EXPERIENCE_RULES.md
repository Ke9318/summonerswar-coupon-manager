# Experience Rules

- **EXP-001:** Mock PASS does not establish live source completeness.
- **EXP-002:** A non-empty scan proves only that something was found, not that the inventory is complete.
- **EXP-003:** Dual parsers over one payload are useful differential checks, not independent source evidence.
- **EXP-004:** Same-origin repeated fetches share cache/origin failure and are weak freshness evidence.
- **EXP-005:** Advertised/reference/production counts are separate signals; investigate both upward and downward mismatches.
- **EXP-006:** First-run state can learn a truncated inventory as normal; protect it with trusted seed, recent observations, grace, and cross-source union.
- **EXP-007:** GUI lifecycle failures can hide successful discovery or crash the app; repeat create-refresh-close-dispose checks when UI/tray ownership changes.
- **EXP-008:** Historical coupon strings are regression evidence only, never production hardcoding.
- **EXP-009:** `SeenCodes` is presentation state; per-account terminal History controls normal execution.
- **EXP-010:** A manual all-results retry can override terminal history by explicit user choice; unattended execution must never invoke it.
