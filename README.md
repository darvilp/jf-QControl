# QControl

QControl is a Jellyfin server plugin under active alpha development that reduces qBittorrent activity
while Jellyfin media is open in a player. It can independently enable
qBittorrent Alternative Limits and stop administrator-selected torrents, then
restore the controlled state after a configurable grace period.

The repository currently contains the loadable Jellyfin 10.11 plugin,
package tooling, isolated Jellyfin/qBittorrent compatibility fixtures,
journaled Stop Torrents and Alternative Limits action slices, and the hosted
Jellyfin playback coordinator. Administrator-only server APIs now validate and
activate configuration, report privacy-safe operational state, and provide
explicit recovery operations. The embedded administrator dashboard exposes
those contracts through native, responsive Jellyfin controls without reading
stored credential content. A new installation remains deliberately inert until
a connection test succeeds and an action is enabled.

## V1 direction

- Jellyfin 10.11 plugin running in-process with the server.
- One qBittorrent 5.2+ Web API endpoint.
- API-key authentication from plugin configuration or an external secret file.
- Playing and paused Jellyfin sessions both count as playback presence.
- Immediate protection and a 60-second default release grace.
- Independent Alternative Limits and Stop Torrents actions.
- Stop all torrents or only selected qBittorrent categories.
- Independently include incomplete and completed torrents.
- Configurable marker and never-touch tags.
- Explicit-hash mutations; never blind `stop all` or `start all`.
- Conservative recovery after a Jellyfin/plugin interruption.
- Windows, native Linux, and containerized Jellyfin support.

## Documentation

- [Domain language](CONTEXT.md)
- [Design specification](docs/DESIGN.md)
- [Architecture decisions](docs/adr/)
- [Testing strategy](docs/TESTING.md)
- [Local development and fixture design](docs/DEVELOPMENT.md)
- [TDD implementation plan](docs/PLAN.md)
- [Issue specifications](docs/issues/)
- [Research and references](docs/research/first-pass.md)
- [Jellyfin 10.11.11 / qBittorrent 5.2.3 compatibility evidence](docs/compatibility/jellyfin-10.11.11-qbittorrent-5.2.3.md)
- [Interruption and outage evidence matrix](docs/compatibility/interruption-and-outage-matrix.md)
- [Alpha installation and release preparation](docs/RELEASE.md)

## Development quick start

```bash
scripts/dotnet.sh restore Jellyfin.Plugin.QControl.sln
scripts/dotnet.sh test Jellyfin.Plugin.QControl.sln --configuration Release
tests/packaging/package-contract.test.sh
scripts/test-issue-001.sh
scripts/test-issue-005.sh
scripts/test-issue-006.sh
scripts/test-issue-007.sh
scripts/test-issue-008.sh
scripts/test-issue-009.sh
scripts/test-issue-010.sh
```

The issue gates run the Docker compatibility suite and use only the
project-owned state below `.testenv/`; Issue 005 additionally exercises real
journaled torrent protection and restoration, Issue 006 adds real Alternative
Limits ownership, and Issue 007 runs the packaged hosted coordinator through
real playing, paused, overlapping, stopped, and disconnected Jellyfin session
shapes. Issue 008 additionally proves administrator authorization, secret-file
connection validation, category discovery, both enabled actions, live status,
credential-free journaling, grace release, and exact restoration through the
packaged plugin. QControl uses loopback ports `18196` and `18180`, so the
TagSync fixture can remain running on `18096`.
Issue 009 adds controller/unit checks, served-resource verification, and a
containerized Chromium smoke of the real administrator page: connection,
configuration, recovery confirmation, keyboard focus order, credential
non-round-trip, and narrow-layout overflow.
Issue 010 adds real web-player activation, a hard Jellyfin interruption,
qBittorrent acquisition/restoration outages, clean temporary-manifest install,
and locally prepared alpha assets. It does not publish a release.

## License

QControl is licensed under GPL-3.0.
