# Issue 002 — Pure activation and policy model

## Behavior

Implement the framework-free domain model that derives playback presence,
reduces protection activation state with a fake clock, selects torrents, and
plans deterministic desired actions.

## Examples

- Playing and paused sessions both qualify; a connected session without media
  does not.
- The last player closing begins 60 seconds of release grace; a new player at
  59.999 seconds cancels release.
- Selected-category, completed-only scope includes completed queued seeds but
  excludes incomplete downloads.
- A torrent carrying both configured tags is excluded.
- All scope returns explicit hashes and never a special `all` token.

## ADRs

- ADR-0002
- ADR-0003

## TDD sequence

1. Start with failing table tests for playback presence.
2. Add fake-clock activation transition tests, including exact boundaries.
3. Add failing torrent-selection tests for all/category, lifecycle, stopped
   state, and tag precedence.
4. Add failing action-plan tests for deterministic explicit hashes and
   Alternative Limits ownership.
5. Add idempotence/property tests and refactor into immutable domain values.

## Acceptance tests

- Every playback, scope, lifecycle, exclusion, grace, and Alternative Limits
  row assigned to the pure layer in `TESTING.md` passes.
- Randomized ordering of neutral sessions and torrents produces the same plan.
- Replanning an already-settled snapshot emits no mutation.
- The domain project has no Jellyfin, HTTP, filesystem, timer, or logging
  dependencies.

## Out of scope

- Real Jellyfin DTO conversion.
- HTTP requests.
- Journal persistence.
- Retry and coordinator scheduling.
