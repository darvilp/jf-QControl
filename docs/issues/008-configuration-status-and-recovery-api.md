# Issue 008 — Configuration, status, and recovery API

## Behavior

Expose administrator-only server contracts for validated configuration,
connection/category discovery, privacy-safe operational status, activation
configuration snapshots, credential replacement, and explicit interruption
recovery.

## Examples

- Saving valid settings during playback succeeds and reports that behavior
  changes apply to the next activation.
- Replacing an invalid API key reconnects the active worker immediately.
- A normal configuration read says only that a key is configured.
- Status identifies a paused player as playback presence without returning its
  user, device, or media title.
- “Resume marked torrents” performs explicit tagged recovery.
- “Mark resolved without changing qBittorrent” clears recovery state and makes
  no qBittorrent request.

## ADRs

- ADR-0002
- ADR-0003
- ADR-0004
- ADR-0005

## TDD sequence

1. Add failing authorization tests for every endpoint.
2. Add candidate configuration validation and revision tests.
3. Add connection test and category-discovery response contracts.
4. Add status DTO privacy and bounded-error tests.
5. Add current-versus-next activation configuration tests.
6. Add explicit recovery command tests, including repeats and failures.
7. Add configuration serialization and schema-migration tests.

## Acceptance tests

- Non-administrators cannot read configuration, status, categories, or execute
  recovery.
- Invalid settings never replace active valid configuration.
- Empty credential input retains a stored key; replace and clear are explicit.
- Neither stored nor file-based keys are returned through any API.
- Status includes all fields specified by `DESIGN.md` and no prohibited display
  data.
- Recovery actions are idempotent and maintain journal/tag ordering.

## Out of scope

- HTML/JavaScript dashboard.
- General qBittorrent administration.
- Automatic collision, admin-action, or public-IP detection.
