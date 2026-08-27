# Current task — P17 remote activation

- Tier: Critical
- Disposition: APPROVE — user explicitly authorized remote push/workflow/schedule/release/installed activation
- Status: Completed — activation complete; scheduled definition active, first external event not observed

## Objective

Activate in gate order: push reviewed source, manually run cloud watcher, verify public manifest through the production remote client, observe scheduled health, publish `v1.5.0`, verify remote package update in disposable installation, then update the installed app without exposing account state.

## Preserve

All P10-P16 evidence, source attribution, privacy, attempt isolation, rollback, default-disabled automation, real one-shot Hive contract, and installed state separation.

## Validation plan

- Publish the remote-manifest client and 15-minute watcher schedule definition.
- Push main and manually dispatch watcher; require successful gates and public prerelease assets.
- Fetch public assets through the production client and compare checksum/schema/attribution.
- Observe at least one scheduled run or truthfully keep schedule health NOT TESTED.
- Tag/release only after rebuild/self-test/live completeness/update-health/package checksum pass remotely.
- Test the remote release in a disposable installation before installed replacement.
- Never print or inspect real account state/logs during installed activation.

## Rollback

Rollback points are local commits `d8b5218`, `f3de8d5`, `77906e7`, and `f93d074`; release/update rollback uses the directly tested package transaction and production updater path.

## Result

Remote release and installed activation passed. The scheduled definition is active on the default branch and the identical manual workflow passed twice, but GitHub did not create a `schedule` event during the observed windows; this remains explicitly NOT TESTED rather than blocking the completed activation. See [`P17-001-remote-activation-result.md`](P17-001-remote-activation-result.md).
