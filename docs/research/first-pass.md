# First-pass research: playback-aware qBittorrent control for Jellyfin

_Research date: 2026-08-13. Confirmed facts are labeled and cited; recommendations and conclusions are explicitly identified as inference._

> [!NOTE]
> This file preserves the initial research snapshot. Product decisions made
> afterward are authoritative in [`../DESIGN.md`](../DESIGN.md) and
> [`../adr/`](../adr/); the open questions and provisional recommendations below
> are not the current specification.

## Executive conclusion

**Confirmed:** this problem has existing implementations. [Speedrr](https://github.com/itschasa/speedrr) is the strongest existing match: it is a GPL-3.0 external service that polls Jellyfin sessions, estimates stream bandwidth, and changes qBittorrent global rate limits; it supports multiple media servers and torrent clients. Its latest `main` commit observed during this research was July 2025. Two smaller exact-purpose tools stop or pause all torrents, but have unsafe restoration behavior and limited compatibility. No qBittorrent/playback controller was found in Jellyfin's current [official 34-plugin catalog](https://repo.jellyfin.org/files/plugin/manifest.json).

**Recommendation (inference):** first decide whether adopting or contributing to Speedrr satisfies the product goal. A new jf-QControl implementation is justified if the differentiators are disk-I/O protection, durable ownership/restoration, webhook latency, explicit qBittorrent 4/5 compatibility, and a small auditable failure model.

For the requested one-server, one-install product, an **in-process Jellyfin plugin is a good fit**: direct session events provide low-latency wake-ups, and `ISessionManager.Sessions` can remain the source of truth. A webhook-assisted external reconciler is the strongest alternative when recovery must continue while Jellyfin is stopped or crashed; that stronger failure isolation costs another service, Jellyfin credentials, and a second configuration surface. A separate message broker is unnecessary in either design.

The **disk-stress** and **bandwidth** policies should stay distinct. Ownership-aware stopping of explicit torrent hashes targets transfer I/O more directly; alternative-rate mode is less disruptive and preserves individual torrent states, but does not establish disk quiescence. The initial action is therefore a product decision rather than an architectural assumption. Neither mode should ever use blind `stop all`, `start all`, or toggle operations.

Verified reference baselines for the first compatibility target are [Jellyfin 10.11.11](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11) and [qBittorrent 5.2.3](https://github.com/qbittorrent/qBittorrent/releases/tag/release-5.2.3). The latter reports WebAPI `2.15.1` in its [release source](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/webapplication.h#L60).

## Existing solutions and adjacent options

### Purpose-built projects

| Project | Confirmed behavior | Assessment (inference) |
|---|---|---|
| [itschasa/speedrr](https://github.com/itschasa/speedrr) | Polls Jellyfin `/Sessions`, distinguishes paused/local playback, estimates Direct Play/Direct Stream/transcode bandwidth, and sets qBittorrent global upload/download limits ([Jellyfin adapter](https://github.com/itschasa/speedrr/blob/f1a8deb8e92ff115855f74be599e53635feb1a37/modules/media_server.py#L233-L274), [qBittorrent adapter](https://github.com/itschasa/speedrr/blob/f1a8deb8e92ff115855f74be599e53635feb1a37/clients/qbittorrent.py#L45-L65)). It supports multiple servers/clients and Docker. | Strongest existing match for **bandwidth** control. It does not establish disk quiescence, and its configured limits become the desired qBittorrent state rather than restoring an independently changed user state. GPL-3.0 affects code reuse. Evaluate deployment before reimplementation. |
| [Aelzaire/Jellyfin-qBittorrent-Monitor](https://github.com/Aelzaire/Jellyfin-qBittorrent-Monitor) | Windows PowerShell service; polls every 30 seconds and uses qBittorrent 5 `torrents/stop` or `torrents/start` with `hashes=all` ([source](https://github.com/Aelzaire/Jellyfin-qBittorrent-Monitor/blob/d4ae28867bb089644987f3e1a8ac904898fadf88/JellyfinQbtMonitor.ps1)). | Unsafe as a base: every previously stopped torrent can be started afterward, and a Jellyfin request failure is converted to “not streaming,” which can immediately restore all torrents. No license file was found, so do not copy its code. |
| [Zouizoui78/jellyfin-qbittorrent](https://github.com/Zouizoui78/jellyfin-qbittorrent) | Receives a Jellyfin Webhook start notification, then polls `/Sessions` every two seconds; at a hard-coded two-session threshold it calls legacy qBittorrent `pause`/`resume` for all torrents ([README](https://github.com/Zouizoui78/jellyfin-qbittorrent/blob/40ef649063b1d5c657bd4f633edc451a4a26258a/README.md), [monitor](https://github.com/Zouizoui78/jellyfin-qbittorrent/blob/40ef649063b1d5c657bd4f633edc451a4a26258a/src/Monitor.cpp)). | Useful proof of the webhook-plus-reconciliation shape, but it counts paused sessions, does not preserve torrent intent, appears to assume qBittorrent authentication bypass, logs the Jellyfin API key, and uses endpoints replaced in qBittorrent 5. Last commit observed: July 2023. |

Speedrr therefore answers “does something already do this?” with **yes for rate limiting**. The smaller tools answer **yes for stop/pause**, but not with a safe ownership contract.

### Adjacent composition

Home Assistant has official [Jellyfin](https://www.home-assistant.io/integrations/jellyfin/) and [qBittorrent](https://www.home-assistant.io/integrations/qbittorrent/) integrations. Jellyfin sessions become media-player entities whose state is derived from `NowPlayingItem` and `PlayState.IsPaused` ([source](https://github.com/home-assistant/core/blob/82807d160491d2edd236639dddfe39a46a18c715/homeassistant/components/jellyfin/media_player.py#L101-L142)); qBittorrent exposes an alternative-speed switch ([source](https://github.com/home-assistant/core/blob/82807d160491d2edd236639dddfe39a46a18c715/homeassistant/components/qbittorrent/switch.py#L27-L36)). If Home Assistant is already present, an automation may be enough for bandwidth control. It must aggregate all sessions so one stop event cannot disable throttling while another session still plays.

The official [Jellyfin Webhook plugin](https://github.com/jellyfin/jellyfin-plugin-webhook) emits playback events but does not control qBittorrent. [GoStream](https://github.com/MrRobotoGit/gostream) is an adjacent example that consumes `PlaybackStart`, `PlaybackStop`, and `PlaybackProgress` webhooks to reprioritize its own torrent engine; it is not a qBittorrent controller.

## Jellyfin event and session facts

The following are confirmed against Jellyfin 10.11.11:

- `ISessionManager` exposes `PlaybackStart`, `PlaybackProgress`, `PlaybackStopped`, `SessionStarted`, `SessionEnded`, and `SessionActivity` events, plus the current session collection ([source](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Controller/Session/ISessionManager.cs#L24-L62)). Playback lifecycle and client-session lifecycle are distinct.
- Clients report start, progress, and stop through authenticated server endpoints, which then call the session manager ([source](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Controllers/PlaystateController.cs#L193-L256)). Consequently, a crashed or disconnected client can omit a clean stop; events alone are not durable truth.
- Authenticated `GET /Sessions` accepts `activeWithinSeconds` and returns session DTOs ([controller](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Controllers/SessionController.cs#L44-L68), [10.11.11 OpenAPI](https://repo.jellyfin.org/files/openapi/stable/jellyfin-openapi-10.11.11.json)). The DTO includes `NowPlayingItem`, `PlayState`, `LastPlaybackCheckIn`, `LastActivityDate`, and `TranscodingInfo`.
- `SessionInfo.IsActive` describes session-controller connectivity, not “media is actively playing”; the playback predicate must use `NowPlayingItem` and `PlayState` ([source](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Controller/Session/SessionInfo.cs#L191-L216)).
- The Webhook plugin registers consumers for playback start, stop, and progress ([source](https://github.com/jellyfin/jellyfin-plugin-webhook/blob/616010ce5e554079f8100bc4a44a89bd54144c93/Jellyfin.Plugin.Webhook/PluginServiceRegistrator.cs#L65-L75)). Its generic destination performs one outbound HTTP send and logs failures; it is not a durable queue ([source](https://github.com/jellyfin/jellyfin-plugin-webhook/blob/616010ce5e554079f8100bc4a44a89bd54144c93/Jellyfin.Plugin.Webhook/Destinations/Generic/GenericClient.cs#L38-L86)).
- Jellyfin's plugin template warns that package-reference versions must match the installed server or the plugin is marked unsupported ([official template](https://github.com/jellyfin/jellyfin-plugin-template#1-initialize-your-project)). The official catalog currently offers Webhook v21 for the 10.11 ABI line ([manifest](https://repo.jellyfin.org/files/plugin/manifest.json)).

Recommended active-playback predicate (inference): `NowPlayingItem != null`, `PlayState != null`, not paused beyond a configurable pause grace, and `LastPlaybackCheckIn` within a freshness window. Do not require `PositionTicks > 0`, because a just-started item legitimately begins at zero. Aggregate across all sessions and apply a short release grace to bridge episode changes, seeks, and late client reports.

In the plugin design, use `ISessionManager` events only to wake a serialized reconciler. Re-read `ISessionManager.Sessions` after every relevant event, at startup, and periodically (initially every 10 seconds). Duplicate, reordered, or missing client reports then affect latency rather than correctness. In the external alternative, Webhook notifications fill the same wake-up role and authenticated `/Sessions` polling supplies the state.

## qBittorrent API facts and semantics

The following are confirmed for qBittorrent 5.2.3/WebAPI 2.15.1 unless noted:

- The WebUI API uses `/api/v2/{group}/{method}`; mutating operations are `POST`, and authentication is required ([official WebUI API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29#general-information)). Username/password login returns an `SID` cookie and requires `Origin` or `Referer` to match the request host and port ([authentication](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29#authentication)).
- qBittorrent 5.2+ / WebAPI 2.14.1+ also supports a single bearer API key; rotation immediately invalidates the old key and the key is not scoped ([official API-key documentation](https://github.com/qbittorrent/qBittorrent/wiki/API-Key-Authentication-%28%E2%89%A5v5.2.0%29)). Cookie auth remains the broadest compatibility choice.
- `GET /api/v2/app/version` and `/api/v2/app/webapiVersion` identify the server and contract. WebAPI minor-version changes can be incompatible by qBittorrent's documented versioning rule ([official API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29#webapi-versioning)).
- In qBittorrent 5, `POST /api/v2/torrents/stop` and `/start` accept pipe-separated hashes or `all` ([5.2.3 implementation](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/api/torrentscontroller.cpp#L1429-L1446)). qBittorrent 4 uses `pause`/`resume`; 5.0 intentionally changed the terminology and endpoints ([5.0 changelog](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/Changelog#L470-L490)).
- Global normal limits are read/set with transfer `downloadLimit`, `uploadLimit`, `setDownloadLimit`, and `setUploadLimit`; values are bytes per second and zero means unlimited. Alternative mode can be read with `speedLimitsMode`. In 5.2.3, `POST transfer/setSpeedLimitsMode` accepts a desired `mode`, avoiding a read-then-toggle race ([implementation](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/api/transfercontroller.cpp#L90-L146)). Older APIs expose only `toggleSpeedLimitsMode`, so code must detect capability.
- The registered 5.2.3 WebAPI mutations include per-torrent start/stop and transfer limits, but no whole-BitTorrent-session pause endpoint ([route table](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/webapplication.h#L210-L245)).

**Important distinction (inference):** alternate/global rate limits control network transfer rates. They may indirectly reduce torrent disk traffic, but they do not stop verification, moves, metadata work, cache flushes, or all competing disk access. Conversely, stopping owned torrents directly targets peer-transfer I/O but still does not prove the disk is idle. The disk-stress objective therefore requires runtime I/O measurement; a bandwidth-only acceptance test is insufficient.

## In-process plugin versus external reconciler

| Concern | In-process Jellyfin plugin | Webhook-assisted external reconciler |
|---|---|---|
| Event latency | Direct event consumers; lowest latency. | Webhook is near-real-time; polling bounds missed-event latency. |
| Truth | Direct `ISessionManager.Sessions`. | Authenticated `/Sessions`, which exposes the required state. |
| Failure isolation | qBittorrent HTTP, credentials, retries, and bugs run inside the media server process. | Isolated process can survive Jellyfin restarts and restore qBittorrent state. |
| Compatibility | Coupled to Jellyfin assemblies/ABI; plugin template requires matching server packages. | Coupled mainly to versioned REST fields; can negotiate and contract-test. |
| Recovery | Stops when Jellyfin stops—the exact moment restoration may be needed. | Can journal ownership, reconcile on its own restart, and observe Jellyfin unavailability. |
| Operations | One install surface in Jellyfin. | Additional service/container, API key, webhook listener, and health reporting. |

**Recommendation (inference):** use the in-process plugin boundary for this project's stated scope, while making its lifecycle limitation explicit: a clean Jellyfin shutdown can restore owned qBittorrent state, but a hard crash can leave qBittorrent constrained until Jellyfin restarts and performs recovery. If restoring during Jellyfin downtime is a hard requirement, choose the external reconciler instead. Avoid a standalone message broker in either case.

## Recommended ownership and restoration state machine

The central invariant is: **jf-QControl may undo only state it can prove it changed.**

1. **Normal:** no lease and no qBittorrent mutation. Reconcile Jellyfin sessions and accept direct event wake-ups (or webhook wake-ups in the external design).
2. **Acquire:** confirm qualifying playback with `/Sessions`; fetch qBittorrent version and current torrent states. Select only currently running torrent hashes. Before changing qBittorrent, atomically persist a lease containing generation ID, mode, acquired time, original alternative-mode state, and owned hashes. Optionally add a visible temporary ownership tag; qBittorrent provides per-torrent add/remove-tag operations ([5.2.3 source](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/api/torrentscontroller.cpp#L1957-L2019)).
3. **Controlled:** stop explicit owned hashes—never `hashes=all`—and read back state before marking the action successful. On reconciliation, journal newly running torrents before acquiring them. Never claim torrents that were already stopped. An exemption tag/config must let an operator opt a torrent out.
4. **Release pending:** when no qualifying playback remains, wait a configurable grace (initial proposal: 30 seconds; longer for paused playback) and re-check Jellyfin.
5. **Restore:** start only hashes in the durable lease that are still stopped and retain the ownership marker; ignore missing/deleted torrents. Remove marker and lease only after read-back confirmation. If ownership is ambiguous, leave the torrent stopped and alert rather than start it.
6. **Recovery/degraded:** on process restart, load the lease before touching qBittorrent. If Jellyfin is active, reassert the controlled state. If Jellyfin is temporarily unavailable, hold current state for a bounded grace rather than immediately restoring. After the configured outage policy expires, restore only owned state—not all torrents. A qBittorrent failure never advances the state machine without read-back confirmation.

qBittorrent does not expose operation provenance: after both the agent and a human stop a torrent, the stopped state alone cannot identify the last actor. A durable journal plus visible ownership tag narrows this race but cannot eliminate it. This residual limitation must be documented and tested. A “remove the ownership tag to cancel auto-restore” convention gives operators an escape hatch.

For a **network-bandwidth** policy, the same state machine stores `original_alt_mode` and enables alternative mode only when it was originally off. Release disables it only if the lease owns that transition and no conflict is detected. On 5.2.3 use deterministic `setSpeedLimitsMode`, not toggle. qBittorrent's scheduler/manual toggle can still conflict, so ownership conflict should alert and preserve current state.

## Failure, safety, and security requirements

- The in-process plugin needs no Jellyfin API credential or webhook listener. If the external alternative is selected, treat webhooks as untrusted hints: bind the listener to the management network, require a high-entropy shared header, cap body size, and never derive outbound URLs from payload data.
- Protect the qBittorrent credential as a secret and never put keys in query strings or logs. An external controller also needs a Jellyfin credential and should use the authorization header. Redact headers, cookies, credentials, and response bodies in diagnostics.
- qBittorrent's credential is effectively administrative and can reach destructive endpoints. Keep WebUI on loopback/private networking; use authenticated TLS when crossing hosts. Do not disable host-header validation, CSRF protection, or authentication bypass to simplify the agent.
- Use bounded connect/request timeouts, exponential retry with jitter, a circuit breaker, and health/status reporting that distinguishes playback state unknown, qBittorrent unreachable, and restoration conflict. The external design additionally reports webhook health.
- Event notifications can be lost, duplicated, delayed, or delivered out of order. State reconciliation must be idempotent and authoritative.
- Multiple simultaneous sessions must be aggregated. Release only when the qualifying-session set is empty after grace.
- A Jellyfin restart, client crash, qBittorrent restart, expired cookie, new torrent during playback, operator intervention, and agent crash at every mutation boundary are required fault cases.

## Compatibility and test strategy

Initial support should be an explicit matrix, not “latest”: Jellyfin 10.11.11 and qBittorrent 5.2.3 first, with feature detection through public version endpoints. Add qBittorrent 4 only after its `pause`/`resume` mapping and auth flow have dedicated contract tests. Reject unknown incompatible WebAPI minor versions unless an explicit compatibility rule exists.

Reuse the repository workstream's jf-tagsync Docker integration-test convention: pinned, ephemeral Jellyfin and qBittorrent containers; isolated config/data volumes; no real media, trackers, or public network dependency; deterministic readiness probes; logs captured on failure; and teardown that cannot touch operator services. Suggested layers:

1. Pure state-machine tests with a fake clock and exhaustive event/failure sequences.
2. HTTP contract tests from recorded 10.11.11 `/Sessions` and 5.2.3 WebAPI payloads, including auth expiry and malformed responses.
3. Container integration tests that create dummy torrent records where possible, exercise direct Jellyfin-event plus session-snapshot reconciliation, and verify exact hashes/limits before and after release. Exercise webhook-plus-poll only if the external alternative is selected.
4. Crash-point tests after journal write, tag, stop, partial stop, and partial restore.
5. Disk-goal validation using measured qBittorrent process/device I/O during representative Jellyfin playback; do not accept only network-rate or API-state evidence.

## Open product decisions

1. Is the primary goal shared-disk contention, WAN upload contention, or both? This selects `stop-owned`, alternate limits, or independent policies.
2. Which playback qualifies: video only, audio too, remote only, transcodes only, selected users/devices, or every unpaused item?
3. How long may a pause or brief session gap last before restoration?
4. What is the default behavior after prolonged Jellyfin unavailability: bounded hold then owned restore, or hold indefinitely?
5. Is a visible temporary qBittorrent ownership tag acceptable? Without one, operator-versus-agent stop races are less observable.
6. Are qBittorrent 4.x, multiple Jellyfin servers, multiple qBittorrent instances, Windows services, and Home Assistant users in the initial support scope?
7. Does Speedrr already meet the bandwidth portion well enough that jf-QControl should focus solely on safe disk-oriented stop/recovery?

## Recommended first implementation slice

After those decisions, build only the plugin's control core for Jellyfin 10.11.11 + qBittorrent 5.2.3: direct session observation, serialized reconciliation, the durable ownership state machine, one selected qBittorrent policy, and fake-service tests. Add a minimal test-connection/status surface, but defer polished UI, multiple media servers, qBittorrent 4, and adaptive bitrate calculations until crash recovery and operator-state preservation are proven.
