# Issue 001 — Repository skeleton and compatibility spike

## Behavior

Establish a buildable, packageable Jellyfin 10.11 plugin skeleton and an
isolated Jellyfin/qBittorrent development environment. Prove the exact runtime
contracts QControl will depend on before production adapters are implemented.

## Examples

- A clean checkout restores, builds, tests, and packages without operator data.
- The empty plugin loads in the pinned Jellyfin container and exposes its
  administrator page placeholder.
- A probe authenticates to pinned qBittorrent with an API key and reads versions,
  categories, torrents, and Alternative Limits state without mutating them.
- Deterministic trackerless fixtures reach stable completed, incomplete,
  stopped, stalled, downloading, and queued representative states without
  public-network participation.
- Test teardown cannot address a non-project container.

## ADRs

- ADR-0001
- ADR-0005

## TDD sequence

1. Add a failing plugin contract test for permanent name, GUID, and embedded
   page discovery.
2. Add the minimal solution, plugin project, and test project to pass it.
3. Add failing package-content and configuration-round-trip tests.
4. Add minimal packaging and configuration DTOs.
5. Create isolated Docker services and perform the bounded compatibility spike.
6. Generate local payload/torrent fixtures, establish each real status through
   bounded API polling, and prove category/tag setup.
7. Record exact results in `docs/compatibility/` and convert stable discoveries
   into automated contract tests where practical.

## Acceptance tests

- Clean restore/build/test commands pass.
- Minimal package loads on the selected Jellyfin 10.11 release.
- qBittorrent version and API-key probes pass against the pinned 5.2 release.
- The pinned qBittorrent image digest and API-key bootstrap procedure are
  recorded without retaining fixture credentials.
- Local web-seed fixtures prove incomplete downloading/queueing, while local
  completed fixtures prove start/stop and seeding behavior.
- Session event/snapshot, path persistence, and qBittorrent mutation contracts in
  `TESTING.md` have recorded outcomes.
- Docker logs are captured on failure and isolated teardown is demonstrated.
- Exploratory code not meeting production standards is removed.

## Out of scope

- Protection state machine.
- qBittorrent mutations from the plugin.
- Finished configuration or status UI.
- Publication to GitHub or a Jellyfin catalog.
