# Issue 009 — Administrator dashboard

## Behavior

Build a thin, accessible Jellyfin administrator page over the validated server
contracts for connection, actions, torrent scope, timing, current status, and
manual recovery.

## Examples

- An administrator selects stored key or secret-file authentication and tests
  the connection without the page ever reading an existing key.
- Stop Torrents cannot be enabled until at least one lifecycle is selected and
  its tag names are valid.
- Category choices come from qBittorrent while temporarily missing configured
  categories remain visible.
- During release grace, status displays a countdown.
- Recovery buttons use plain language and explain their qBittorrent effects
  before confirmation.

## ADRs

- ADR-0003
- ADR-0004
- ADR-0005

## TDD sequence

1. Add embedded-resource and page-contract tests.
2. Add browser tests for configuration rendering and server validation errors.
3. Add credential non-round-trip and source-switching tests.
4. Add live-status and accessible announcement tests.
5. Add recovery confirmation and error-state tests.
6. Run a browser smoke against the pinned Jellyfin container.

## Acceptance tests

- Every action and validation is backed by an administrator-only server API.
- JavaScript does not reimplement protection semantics.
- Existing credentials never enter DOM, network responses, screenshots, or
  browser logs.
- Keyboard-only configuration and recovery are usable.
- Dynamic status uses accessible live regions without repeated noisy
  announcements.
- Desktop and narrow Jellyfin dashboard layouts remain usable.

## Out of scope

- Non-administrator UI.
- Torrent-name or media-title display.
- A general qBittorrent dashboard.
- Custom visualization or branding work beyond a clear QControl identity.
