# Issue 004 — Durable activation journal

## Behavior

Persist activation intent and progress atomically beside Jellyfin plugin
configuration, detect interrupted activations, and expose neutral recovery
state without storing sensitive display data.

## Examples

- A valid journal round-trips after process restart.
- Failure while writing a replacement leaves the prior valid journal readable.
- A truncated or unsupported journal does not authorize any automatic mutation.
- The journal records Alternative Limits ownership and per-hash mutation stages
  but not torrent names, media titles, or credentials.

## ADRs

- ADR-0004

## TDD sequence

1. Add failing round-trip tests for a versioned journal DTO.
2. Add failing write-order tests using a fault-injecting filesystem port.
3. Implement temporary-file, flush, and atomic replacement semantics.
4. Add incompatible, corrupt, and interrupted-state loading tests.
5. Add structural secret/privacy scans over serialized fixtures.

## Acceptance tests

- Every journal and interruption row in `TESTING.md` assigned to persistence
  passes.
- No accepted read can observe half a new document.
- The resolved path uses Jellyfin's runtime plugin-configuration directory and
  never the plugin binary or cache directory.
- Journal schema migration behavior is explicit from version one.
- Windows and Unix path construction tests pass.

## Out of scope

- Applying qBittorrent mutations.
- Automatic crash restoration.
- Long-term operational history.
- Encryption or storage of credentials.
