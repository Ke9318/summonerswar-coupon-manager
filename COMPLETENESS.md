# Coupon source completeness design

## Why v1.3.3 could pass while codes were missing

The live test only required a non-empty merged result and at least one successful source. It never compared a source's full explicit inventory with that source's production extraction. A partial SWGT or SW-Teams result therefore passed. The v1.3.3 test change also removed the earlier `INVOCATEUREU26` and `SWCTICKET2HAMBURG` parser examples while replacing the broad visible-text fallback with narrower explicit/context patterns. There was no source-count regression gate to expose either change.

## Independent inventories

- SWGT reference: code path segment from canonical `withhive.me/313/...` anchors. Production uses Hive URL matching plus code attributes, link classes, tables, and coupon context.
- SW-Teams reference: visible `<code>` elements. Production combines explicit code elements, attributes, tables, JSON fields, and coupon context.
- SWQ reference: cells explicitly marked `code-cell`. Production locates the code column of coupon tables.
- GitHub Manual reference: independent JSON traversal of every entry. Production uses its own remote-candidate extraction path.

Reference extraction does not call production extraction or reuse its regex instances/selectors. Each successful fetch records payload bytes, production count, reference count, missing codes, and extra candidates. A recognizable source shell is required so an empty result caused by a redesign is not treated as a verified empty inventory.

## Captures

`test-fixtures/live-captures` contains the complete HTTP payloads captured on 2026-08-20, rather than simplified hand-written markup. Offline self-test calculates the same source-by-source difference against these payloads. The historical code strings remain explicit regression cases, but they are not represented as live on 2026-08-20 after the upstream lists removed them.

## Release behavior

The tag workflow builds, publishes, runs state/retry tests plus offline captured-source regression tests, and finally runs the live completeness gate. Network failure is reported as unverifiable; a successful response with a non-empty missing set is reported as a parser regression. Either result blocks ZIP and GitHub Release creation.
