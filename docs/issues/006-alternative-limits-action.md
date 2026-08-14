# Issue 006 — Alternative Limits action

## Behavior

Add Alternative Limits as an independent action that records the initial mode,
enforces enabled mode during protection, and restores only a transition made by
the uninterrupted activation.

## Examples

- Initially disabled mode is enabled and later disabled on normal release.
- Initially enabled mode remains enabled after release.
- Manual or scheduled disable during playback is re-enabled on reconciliation.
- Alternative Limits can protect without Stop Torrents.
- When both actions are enabled, failure in one does not invent successful state
  for the other.

## ADRs

- ADR-0002
- ADR-0003
- ADR-0004
- ADR-0005

## TDD sequence

1. Add failing application tests for initial-disabled and initial-enabled mode.
2. Require journal persistence before the deterministic set operation.
3. Add enforcement and idempotence tests.
4. Add normal-release and partial-failure tests.
5. Compose with the Stop Torrents action and exercise both operation orders.
6. Prove deterministic behavior against pinned qBittorrent.

## Acceptance tests

- Every Alternative Limits and combined-action row in `TESTING.md` passes.
- The toggle endpoint is never called.
- Alternative limit values are never modified.
- Normal release changes mode only when the activation owns the transition.
- Interrupted state never automatically restores the mode.

## Out of scope

- Configuring alternative upload/download values.
- Adaptive or per-torrent rate limiting.
- Interpreting qBittorrent scheduler or administrator intent.
