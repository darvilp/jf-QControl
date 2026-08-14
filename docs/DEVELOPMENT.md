# Local development

QControl has no production code yet. Issue 001 will add exact restore, build,
test, package, and environment commands after they are proven from a clean
checkout.

## Prerequisites

- Git
- .NET SDK 9
- Docker Engine with Compose
- A browser for administrator-page smoke testing

The local workstation used for initial planning has a working .NET 9, Docker,
and Compose installation. The repository must not depend on the developer's
existing Jellyfin or qBittorrent services.

## Isolation contract

The test environment follows the established `jf-TagSync` conventions:

- explicit Compose project and container names;
- pinned images;
- loopback-only host ports;
- isolated configuration, cache, fixture, and download volumes under
  `.testenv/`;
- generated local media and torrent fixtures;
- deterministic readiness probes and bounded state waits;
- logs captured on failure;
- teardown scoped to the Compose project;
- no public tracker, DHT, PeX, LPD, UPnP, or operator-service dependency.

## Planned Compose topology

```text
Jellyfin 10.11
      │ qBittorrent Web API
      ▼
qBittorrent 5.2 under test
      │
      ├── isolated config/download volumes
      ├── local throttled HTTP web seed
      └── optional local peer fixture
```

The qBittorrent image will be pinned by version and digest after Issue 001
proves the packaged application and Web API versions. `latest` is not an
accepted CI dependency.

The WebUI is reachable from Jellyfin as `http://qbittorrent:8080` and may be
published on a loopback-only high port for diagnostics. No BitTorrent listening
port is published to the host.

## API-key bootstrap

The fixture harness may use a deterministic test-only WebUI username/password
to initialize qBittorrent, but QControl itself never receives those values.

Bootstrap flow:

1. Start qBittorrent with isolated fixture configuration.
2. Log in through the Web API from the fixture harness.
3. Rotate/generate one ephemeral qBittorrent API key.
4. Write it to a Compose-only secret file.
5. Mount that file read-only into Jellyfin.
6. Configure QControl to use the secret-file credential source.

The credential and temporary WebUI password must never appear in repository
files, normal test output, captured screenshots, or retained artifacts.

## Torrent fixtures

The suite generates small deterministic payloads and trackerless v1 `.torrent`
files. A local throttled web server supplies content when an observable download
is required. A second local peer is optional and used only when actual uploading
adds evidence that stable completed/seeding states cannot provide.

Stable real-container fixtures include:

| Condition | Construction |
|---|---|
| Completed/seeding | Matching payload exists; add and recheck torrent |
| Completed/stopped | Complete fixture is explicitly stopped |
| Incomplete/stalled | Payload absent; no tracker, peer, or web seed |
| Incomplete/downloading | Payload absent; local throttled web seed available |
| Incomplete/queued | Active-download limit plus several local web-seed torrents |
| Completed/queued | Active-seed limit plus several completed torrents |
| Category scope | Assign `sonarr`, `radarr`, `manual`, or no category |
| Marker/exclusion | Assign configured Marker or Never-touch Tags |

The suite polls qBittorrent with bounded deadlines and asserts each fixture's
precondition before exercising QControl. Fixed sleeps are not acceptance proof.
Transient states such as checking, moving, and metadata download are covered by
deterministic client/domain contract cases unless a real compatibility finding
shows that their stop behavior differs materially.

## Jellyfin playback fixtures

Most integration scenarios act as a small authenticated Jellyfin client and use
the real playback-reporting endpoints to produce playing, paused, overlapping,
and stopped session state. This exercises real `ISessionManager` events and
snapshots without performing a transcode in every test.

At least one browser/client smoke uses a small generated local media file to
prove the complete player-to-plugin path.

## Safety

Never point test configuration at a non-loopback host or an existing
qBittorrent download directory. Environment start scripts must validate the
Compose project name, resolved paths, and container labels before teardown or
fault injection.
