# Local development

QControl targets .NET 9 and Jellyfin 10.11.11. The repository-local wrapper
keeps disposable .NET and NuGet state below ignored `.testenv/` paths.

## Build and test

```bash
scripts/dotnet.sh restore Jellyfin.Plugin.QControl.sln
scripts/dotnet.sh build Jellyfin.Plugin.QControl.sln --configuration Release --no-restore
scripts/dotnet.sh test Jellyfin.Plugin.QControl.sln --configuration Release --no-build --no-restore
tests/packaging/package-contract.test.sh
tests/fixtures/qbittorrent-client-contract.test.sh
tests/fixtures/qbittorrent-action-contract.test.sh
```

Run the complete repository-skeleton and compatibility gate with:

```bash
scripts/test-issue-001.sh
```

That gate runs shell lint, .NET tests, package verification, static fixture
checks, the teardown ownership proof, real qBittorrent state/mutation probes,
real Jellyfin session snapshots, and packaged-plugin installation.

Run the qBittorrent client and credential gate with:

```bash
scripts/test-issue-003.sh
```

The qBittorrent client contract command starts the isolated stack, creates six
dummy torrents, and runs the production C# adapter through every V1 endpoint.
It restores the selected torrent's stopped state, its temporary tag, and the
initial Alternative Limits mode before teardown.

Run the complete Stop Torrents application slice with:

```bash
scripts/test-issue-005.sh
```

Its action contract uses the production client, physical journal store, and
serialized Stop Torrents service against the six real qBittorrent fixtures. It
protects every initially running non-excluded hash, proves already-stopped and
Never-touch fixtures remain untouched, restores only marked hashes, and then
returns the fixture to its initial stopped/running shape.

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

## Compose topology

```text
loopback 18196/18180
          │
          ▼
HTTP-only gateway
          │ internal network
          ├── Jellyfin 10.11.11
          ├── qBittorrent 5.2.3
          └── local HTTP web seed
```

All three images are pinned by version and registry digest in `compose.yaml`.
`latest` is not an accepted dependency.

The WebUI is reachable from Jellyfin as `http://qbittorrent:18180` and from the
host as `http://127.0.0.1:18180`. Jellyfin is exposed at
`http://127.0.0.1:18196`. The gateway forwards only HTTP; no BitTorrent
listening port is published or routed.

## API-key bootstrap

The fixture harness may use a deterministic test-only WebUI username/password
to initialize qBittorrent, but QControl itself never receives those values.

Bootstrap flow:

1. Start qBittorrent with isolated fixture configuration and the WebUI on port
   `18180` both inside and outside the service boundary.
2. Log in with either the fixture password or the one-time startup password.
3. Rotate/generate one ephemeral qBittorrent API key.
4. Write it to a Compose-only secret file.
5. Mount that file read-only into Jellyfin.
6. Configure QControl to use the secret-file credential source.

The credential and temporary WebUI password must never appear in repository
files, normal test output, captured screenshots, or retained artifacts.

## Activation journal

The runtime journal path is always resolved from Jellyfin's
`PluginConfigurationsPath` and ends in
`Jellyfin.Plugin.QControl.journal.json`. It never uses the plugin binary or
cache directory. The store writes a uniquely named temporary file in that same
directory with write-through enabled, flushes it, and atomically replaces the
prior document. Native Unix files are mode `0600`; Windows files inherit the
Jellyfin service account's configuration-directory ACL.

Schema version 1 stores only activation identifiers, session IDs, the behavior
snapshot, credential-free endpoint identity, bounded failure codes, and action
progress. It has no free-form diagnostic or display-name fields. A valid
journal owned by another process grants protect-only authority; corrupt or
unsupported documents grant no automatic mutation authority.

Run its full repository gate with:

```bash
scripts/test-issue-004.sh
```

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
| Category scope | Assign `sonarr` and `radarr`; compatibility probes also round-trip spaces and non-ASCII names |
| Marker/exclusion | Assign configured Marker or Never-touch Tags |

The suite polls qBittorrent with bounded deadlines and asserts each fixture's
precondition before exercising QControl. Fixed sleeps are not acceptance proof.
Transient states such as checking, moving, and metadata download are covered by
deterministic client/domain contract cases unless a real compatibility finding
shows that their stop behavior differs materially.

The Stop Torrents application tests additionally fault partial tag, stop/start,
and marker-removal progress through the public qBittorrent and journal seams.
They assert that durable intent precedes every external mutation, only
read-back-confirmed marked hashes can be stopped, and repeated reconciliation
reaches a mutation-free fixed point.

## Jellyfin playback fixtures

Most integration scenarios act as a small authenticated Jellyfin client and use
the real playback-reporting endpoints to produce playing, paused, overlapping,
and stopped session state. This exercises real `ISessionManager` events and
snapshots without performing a transcode in every test.

A browser/client smoke remains part of the alpha hardening issue. Issue 001
proves the same server-side session shapes through authenticated playback
reports and a generated four-second media file.

## Safety

Never point test configuration at a non-loopback host or an existing
qBittorrent download directory. Environment start scripts must validate the
Compose project name, resolved paths, and container labels before teardown or
fault injection.
