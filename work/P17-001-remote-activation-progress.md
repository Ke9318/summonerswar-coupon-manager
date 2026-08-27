# P17-001 progress — remote activation

## Activated

- Source main pushed through `de78724`.
- Cloud watcher workflow includes a 15-minute schedule and stable `candidate-manifest` prerelease publication.
- Manual watcher run `33031875743` completed successfully on commit `de78724`.
- Production remote client fetched and validated the public checksum/schema/version contract: schema 1, minimum client 1.5.0, 22 candidates, all four required source names, six-hour validity.

## Evidence

- TESTED: remote checkout/restore/build/self-test/live gated manifest/artifact upload/stable prerelease upload.
- TESTED: public asset checksum-before/JSON/checksum-after and client parser validation, exit 0.
- NOT TESTED: scheduled-event run, v1.5.0 release workflow, remote release update, installed replacement.

## Next

Push the remote-gate command, create/push `v1.5.0`, require the release workflow to pass, then test the remote release against a disposable installation before installed activation.
