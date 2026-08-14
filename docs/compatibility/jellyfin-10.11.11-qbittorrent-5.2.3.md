# Jellyfin 10.11.11 / qBittorrent 5.2.3 compatibility

Status: **passed on 2026-08-14**

This report records the Issue 001 compatibility spike. It proves runtime and
Web API assumptions used by later production adapters; it does not claim that
the protection state machine is implemented.

## Pinned environment

| Component | Observed version | Pinned image digest |
|---|---|---|
| Jellyfin | `10.11.11` / plugin ABI `10.11.11.0` | `jellyfin/jellyfin@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db` |
| qBittorrent | `v5.2.3`, Web API `2.15.1` | `lscr.io/linuxserver/qbittorrent@sha256:6816d2b144b1eb97665f886e41e18a14d026ba78c9d0953fc68a1211ea819433` |
| HTTP gateway/web seed | nginx `1.28.0` | `nginx@sha256:30f1c0d78e0ad60901648be663a710bdadf19e4c10ac6782c235200619158284` |
| Host tooling | Docker Engine `29.7.2`, Compose `5.4.0`, .NET SDK `9.0.119` | n/a |

The registry index digest is recorded above. The observed LinuxServer build was
`5.2.3_v2.0.14-ls471`.

## Isolation result

- Jellyfin, qBittorrent, and the web seed have only an internal Docker network.
- A dual-homed nginx gateway exposes only HTTP on loopback ports `18196` and
  `18180`; it exposes no peer port and provides no generic forward proxy.
- DHT, PeX, LPD, UPnP, random ports, and the embedded tracker were disabled
  before torrents were added.
- Every torrent was generated locally as trackerless BitTorrent v1 metadata.
- The only content source was the internal nginx web seed.
- Real teardown refused a deliberately created same-name container without the
  `qcontrol-test` Compose project and `io.qcontrol.fixture=true` labels. The
  foreign container still existed after the refusal.
- Failure-log capture and the interactive logs command redact qBittorrent's
  one-time startup password.

## Jellyfin contracts

The JPRM package initially contained only `Jellyfin.Plugin.QControl.dll` and
`meta.json`. Issue 002 added the framework-free
`Jellyfin.Plugin.QControl.Domain.dll`; package contract tests require exactly
those two assemblies and `meta.json`.
Jellyfin logged `Loaded plugin: QControl 0.1.0.0`, returned it as `Active` from
`GET /Plugins`, returned schema version 1 from its configuration endpoint, and
served `Jellyfin.Plugin.QControl.Configuration.configPage.html` through
`/web/ConfigurationPage?name=QControl`.

The runtime configuration path was:

```text
/config/plugins/configurations/Jellyfin.Plugin.QControl.xml
```

The intended journal sibling path persisted across a Jellyfin restart in the
bind-mounted fixture:

```text
/config/plugins/configurations/Jellyfin.Plugin.QControl.journal.json
```

The compatibility test removed its journal sentinel after the persistence
check. Journal serialization and atomic replacement remain Issue 004 work.

Reflection against the exact 10.11.11 controller assembly confirmed
`ISessionManager` exposes `PlaybackStart`, `PlaybackProgress`,
`PlaybackStopped`, `SessionStarted`, `SessionEnded`, and the `Sessions`
snapshot.

Two independently authenticated playback-reporting clients then proved these
snapshot shapes on the real server:

- playing: `NowPlayingItem` present and `PlayState.IsPaused == false`;
- paused: `NowPlayingItem` remains present and `PlayState.IsPaused == true`;
- overlapping: both device sessions are simultaneously enumerable;
- stopped: stopping one clears its current item without clearing the other;
- disconnected: logout removes that device session from the snapshot.

This validates the design choice that events are wake-ups and a fresh complete
session snapshot is authoritative.

## qBittorrent authentication and reads

The LinuxServer image requires the configured WebUI port to match the container
port. Mapping a different host port produced HTTP 401 on correct credentials;
using `18180` end-to-end produced a successful login.

The fixture logged in only long enough to call `app/rotateAPIKey`, validated the
returned `qbt_` key shape, replaced the WebUI password with a test-only value,
and wrote the API key to an ignored mode-`0600` file. The file was mounted
read-only into Jellyfin. A `curl` executed inside the Jellyfin container used
that file to read `app/version` from `http://qbittorrent:18180`, proving the
future plugin's network and bearer-authentication path without retaining the
key.

Read-only probes successfully returned application version, Web API version,
Alternative Limits state, categories, tags, and torrent information.

Issue 003 then ran the production typed client against the same container using
the mode-`0600` secret file. The adapter accepted application `5.2.3` and Web
API `2.15.1`, returned only neutral policy fields (hash, category, bytes
remaining, stopped state, and tags), and never exposed torrent names.

## Stable torrent states

| Fixture | Observed qBittorrent state | Progress |
|---|---|---:|
| `complete-seeding.bin` | `stalledUP` | `1` |
| `complete-stopped.bin` | `stoppedUP` | `1` |
| `incomplete-stopped.bin` | `stoppedDL` | `0` |
| `incomplete-stalled.bin` | `stalledDL` | `0` |
| `incomplete-downloading.bin` | `downloading` | between `0` and `1` |
| `incomplete-queued.bin` | `queuedDL` | `0` |

The fixture used a 64 KiB/s per-torrent limit to keep the downloading case
observable and two active download slots to keep the queued case stable. Every
precondition was established through bounded Web API polling.

The torrent list exposed all selector inputs needed later: hash, category,
tags, progress/completion, and stopped/running state. Categories `radarr` and
`sonarr` and tags `fixture` and `jfNeverTouch` round-tripped exactly.

## Mutation contracts

Against explicit hashes only, qBittorrent accepted and confirmed:

- enable and disable Alternative Limits through the deterministic get/toggle/get
  fixture sequence and the production client's `setSpeedLimitsMode` setter;
- category assignment and restoration;
- tag creation, assignment, removal, and deletion;
- `torrents/start` from `stoppedDL` to a non-stopped accepted state;
- `torrents/stop` back to a stopped state;
- category and tag names containing spaces and non-ASCII characters
  (`TV Épisodes`, `täg`).

Queue promotion was reproduced by the stable fixture set. The tests never send
qBittorrent's special `all` value.

## Commands

```bash
tests/fixtures/environment-contract.test.sh
tests/fixtures/teardown-guard.test.sh
tests/fixtures/qbittorrent-compatibility.test.sh
tests/fixtures/qbittorrent-mutations.test.sh
tests/fixtures/qbittorrent-client-contract.test.sh
tests/fixtures/jellyfin-session-snapshots.test.sh
tests/fixtures/jellyfin-compatibility.test.sh
```

`scripts/test-issue-001.sh` runs these together with restore, build, unit tests,
shell lint, and package verification.

## Deferred boundaries

- The durable journal begins in Issue 004.
- In-process event subscription and serialized reconciliation begin in Issue
  007; Issue 001 proves the exact event surface and runtime snapshot shapes.
- The credential source uses platform-native .NET file APIs without Unix path
  parsing, but a native Windows secret-file smoke cannot be claimed from this
  Linux host. The Windows alpha procedure must verify a Jellyfin-service-readable,
  ACL-protected key file before cross-platform readiness is claimed.
- A real browser/player smoke remains in Issue 010.
