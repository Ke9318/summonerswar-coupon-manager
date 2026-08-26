# Current task — governance and architecture inventory

- Tier: Standard
- Disposition: APPROVE
- Status: Complete

## Scope

Inspect the supplied distribution, locate the maintainable source, inventory architecture and operational concerns, and add a compact AI collaboration layer. Do not change product behavior or user data.

## Result

The supplied `SWCouponManager-win-x64` directory is a published .NET/WinForms distribution. The maintainable source was uniquely identified as `Ke9318/summonerswar-coupon-manager` and cloned beside it. Governance files were added to the source; the distribution receives only a source/governance pointer.

Architecture observed in source:

- discovery and source-health: `CouponSourceService.cs`, `ReferenceInventoryService.cs`, `TrustedInventoryService.cs`, JSON candidate/seed data
- normalization/models/policy state: `Models.cs`, queue and classification helpers in `MainForm.cs`
- account execution and GUI: `MainForm.cs` with WebView2 against the official Hive page
- persistence: `AppStorage.cs` under `%LOCALAPPDATA%\SWCouponManager`, with state backup
- updater: `GitHubUpdateService.cs` and `.github/workflows/release.yml`
- validation: `SelfTest.cs`, captured source fixtures, validation evidence, live release gate

## Validation

- TESTED: repository and distribution inventory; governance file presence/content checks; existing offline self-test after documentation-only changes.
- NOT TESTED: live coupon sources, real account redemption, GUI interaction, release publishing, installed self-update, update rollback.
- INFERRED: source coverage has strong existing regression controls; updater rollback/integrity is incomplete because the apply script overwrites in place without a pre-update install backup or artifact checksum/signature verification.

No product behavior, account data, credentials, logs, source fixtures, or release artifacts were changed.
