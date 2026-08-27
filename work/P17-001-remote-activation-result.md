# P17-001 result — remote activation

## Activated

- `main` pushed through `21c54d1`; `v1.5.0` points to that reviewed commit.
- Release workflow `33032126053` passed restore, version match, publish, self-test, captured-source regressions, staged update health, live source completeness, package inventory, ZIP checksum, and release publication.
- Public release: `https://github.com/Ke9318/summonerswar-coupon-manager/releases/tag/v1.5.0`.
- Release ZIP SHA-256: `b630e403a96e0871b1eb4bbd2e356cec0222475bcd90d9275a8626104c1c15ab`.
- The production remote-manifest client validated the stable `candidate-manifest` assets: schema 1, minimum client 1.5.0, 22 candidates, four required source names, and six-hour validity.
- The public release updated a disposable installation successfully; injected post-copy failure restored every pre-update file hash.
- Workspace deployment `app` moved from 1.4.6 to `1.5.0+21c54d17869a45ec08317a171cdbab4e2492489a`; checksum and synthetic GUI smoke gate passed.
- Pre-activation `app` was preserved at `review-work/app-pre-v1.5.0-20260827-111046`.

## Privacy and safety

- No real Hive ID, account state, credential, or real user log was inspected or emitted.
- Installed activation used a synthetic data directory for the restart gate.
- The attempted 1.4.6 CLI activation was stopped without file replacement after confirming that legacy build did not implement the new command.

## Evidence status

- TESTED: manual cloud-watcher workflow and stable prerelease publication (`33031875743`).
- TESTED: production public manifest checksum/parser gate.
- TESTED: release workflow and public ZIP/checksum publication (`33032126053`).
- TESTED: public remote package normal update and injected-failure rollback in disposable installations.
- TESTED: installed package replacement and synthetic GUI startup.
- PENDING: first `schedule`-event cloud-watcher run; do not treat the schedule as healthy until a successful event is observed.

The initial `*/15` definition produced no schedule event while the workflow and repository Actions policy were both active. Following GitHub's documented recommendation to avoid common high-load boundaries, the same 15-minute cadence was offset to minutes 13, 28, 43, and 58 pending direct observation.
