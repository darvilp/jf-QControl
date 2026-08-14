# QControl

QControl is a planned Jellyfin server plugin that reduces qBittorrent activity
while Jellyfin media is open in a player. It can independently enable
qBittorrent Alternative Limits and stop administrator-selected torrents, then
restore the controlled state after a configurable grace period.

The project is currently in its design and test-planning phase. It contains no
production plugin code yet.

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

## License

The public plugin will use GPL-3.0-compatible licensing in line with the
Jellyfin plugin ecosystem. The license file will be added with the repository
skeleton before production code begins.
