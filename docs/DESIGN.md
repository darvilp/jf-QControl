# QControl — Design Specification

## 1. Product statement

QControl is a Jellyfin server plugin that temporarily reduces qBittorrent
activity whenever any Jellyfin session has media open in its player.

It offers two independent protection actions:

```text
Jellyfin playback presence
          │
          ├── enable qBittorrent Alternative Limits
          │
          └── mark and stop selected torrents
```

Protection begins immediately. When the last player closes, QControl waits for
a configurable release grace before restoring the qBittorrent state under its
control. Playback returning during grace cancels the release.

## 2. Goals

1. Reduce shared-disk and network contention during Jellyfin use.
2. React quickly to Jellyfin playback without a webhook or companion service.
3. Allow Alternative Limits, Stop Torrents, or both.
4. Preserve already-stopped, unmarked torrents.
5. Make torrent ownership visible and controllable through qBittorrent tags.
6. Tolerate torrent queue promotion and torrents added during playback.
7. Restore only after the complete Jellyfin playback-presence set is empty.
8. Preserve enough durable context to explain and manually resolve partial work.
9. Keep all control semantics testable without live Jellyfin or qBittorrent.
10. Support Windows, native Linux, and containerized Jellyfin installations.

## 3. Non-goals for V1

- More than one Jellyfin server or qBittorrent instance.
- qBittorrent before 5.2 or username/password authentication.
- An external controller, webhook receiver, or message broker.
- Automatic restoration after a Jellyfin/plugin process interruption.
- Detecting or preserving administrator start/stop actions during protection.
- User, device, remote/local, media-kind, transcode, or bandwidth-based playback
  filters.
- Treating paused playback differently from advancing playback.
- Per-torrent rate-limit management.
- Measuring disk utilization or proving physical-disk quiescence.
- Public-IP, VPN-exit, or torrent leak detection.
- Multiple policy profiles or time-of-day rules.
- An option to ignore TLS certificate errors.
- A general Jellyfin secrets-manager plugin.

## 4. Compatibility target

V1 targets:

- Jellyfin 10.11, with an exact supported ABI recorded by each plugin package;
- qBittorrent 5.2 or newer with Web API-key authentication;
- one qBittorrent endpoint reachable from the Jellyfin process;
- HTTP on a trusted LAN/container network or HTTPS with normal certificate
  validation.

QControl reads qBittorrent application and Web API versions during connection
testing and startup. An unsupported version prevents protection actions but
does not prevent Jellyfin from running.

## 5. Playback semantics

### 5.1 Playback presence

A Jellyfin session contributes playback presence when it has a current media
item open in the player. Its paused flag does not change that answer.

The predicate deliberately does not inspect:

- media type;
- playback method;
- transcode status;
- user or device;
- local or remote address;
- playback position.

Multiple Jellyfin sessions form one set. The set, not an individual event,
decides whether protection is required.

### 5.2 Events and truth

Playback start, progress, stop, and relevant session events are wake-up hints.
Event handlers enqueue reconciliation and return; they never call qBittorrent.

Each reconciliation rereads Jellyfin's current session collection. Duplicate,
late, reordered, or missed events can delay a pass but cannot directly produce
an inverse qBittorrent mutation.

### 5.3 Protection activation

The lifecycle is:

```text
Inactive
   │ playback presence appears
   ▼
Protecting ───────────────────────────────┐
   │ playback presence disappears         │ presence returns
   ▼                                      │
Release Pending ──────────────────────────┘
   │ release grace expires with no presence
   ▼
Restoring
   │ all enabled actions settle
   ▼
Inactive
```

Protection applies immediately. The configurable release grace defaults to 60
seconds. There is no separate pause grace because paused media remains playback
presence.

An activation takes an immutable snapshot of behavior configuration. Ordinary
configuration edits are saved conveniently but apply to the next activation.
Credential replacement may reconnect immediately without changing the behavior
snapshot.

### 5.4 Reconciliation cadence

The coordinator rereads Jellyfin sessions every 5 seconds in addition to event
wake-ups, including while inactive. It does not contact qBittorrent while
inactive unless status or recovery requires it. During protection the same pass
catches newly added torrents, queue promotion, manual restarts, and missed
Jellyfin events.

When qBittorrent is unavailable, QControl retains its current activation and
retries every 15 seconds. It does not interpret communication failure as a
reason to release.

## 6. Protection actions

The administrator independently enables:

- **Alternative Limits**
- **Stop Torrents**

Both disabled is a valid inert configuration. A new installation performs no
qBittorrent mutation until a connection test has succeeded and at least one
action has been explicitly enabled.

### 6.1 Alternative Limits

On acquisition, QControl reads the current Alternative Limits state.

- If already enabled, QControl records that it did not change the state.
- If disabled, QControl enables it and records ownership of that transition.

While protection remains active, reconciliation re-enables Alternative Limits
if necessary. QControl does not interpret manual or scheduled toggles.

On a normal release, QControl disables Alternative Limits only when the active
journal says this activation changed it from disabled to enabled. If the mode
was originally enabled, it remains enabled.

The administrator configures alternative upload and download values in
qBittorrent. QControl controls the mode, not the values.

### 6.2 Stop Torrents configuration

Stop scope is either:

- **All torrents**, including categorized and uncategorized torrents; or
- **Selected categories**, using exact qBittorrent category names.

Lifecycle selection has two independent switches:

- include incomplete torrents;
- include completed torrents.

At least one lifecycle must be selected when Stop Torrents is enabled.
Completion is classified from remaining content, not transient states such as
queued, stalled, downloading, or uploading.

Two configurable tags participate:

- Marker Tag, default `jfStopped`;
- Never-touch Tag, default `jfNeverTouch`.

Tag names must be non-empty and distinct when Stop Torrents is enabled. Existing
Marker Tag assignments are intentional administrator state. QControl performs
no collision warning, adoption prompt, or provenance inference.

### 6.3 Acquisition

QControl lists torrent state and calculates eligible torrents locally. A
torrent is eligible when:

1. It falls inside the configured stop scope.
2. Its completion lifecycle is selected.
3. It does not carry the Never-touch Tag.
4. It is not already stopped.

For each explicit batch of eligible hashes:

1. Persist intent in the journal.
2. Add the Marker Tag.
3. Confirm or record the tag operation.
4. Stop those same hashes.
5. Confirm or record the stop operation.

QControl never changes a torrent's category. Categories may control save paths
and automatic torrent management, so they are selection metadata only.

### 6.4 Queueing and ongoing enforcement

The initial pass includes eligible queued and stalled torrents, not only those
currently transferring. Periodic reconciliation catches torrents added later
or promoted after other torrents stop.

An eligible torrent that starts again during the activation is stopped again.
QControl intentionally does not determine whether that start came from an
administrator, qBittorrent queueing, an automation, or a restart.

The Never-touch Tag is the only V1 exclusion. It takes precedence over the
Marker Tag. QControl performs no start, stop, tag removal, or other torrent
mutation on a never-touch torrent.

### 6.5 Why “all” still uses explicit hashes

QControl never sends qBittorrent's literal `stop all` or `start all` command.

For all-torrent scope it lists every torrent, filters eligibility, adds the
Marker Tag to explicit hashes, and stops those same hashes. This avoids the race
where a torrent appears after tagging but before a blind stop-all request and
would otherwise be stopped without a marker.

### 6.6 Normal restoration

After release grace expires, QControl queries torrents carrying the activation's
Marker Tag. For explicit hashes that do not carry the Never-touch Tag, it:

1. Records restart intent.
2. Starts the hashes.
3. Reads back that each hash is no longer deliberately stopped.
4. Removes the Marker Tag.

The Marker Tag is authoritative administrator intent. A marked torrent may be
restored even if its tag predates the current installation or activation.
Already-stopped torrents without the marker are never started.

A successful start may leave a torrent queued under qBittorrent's own queueing
rules. QControl does not require immediate data transfer; read-back must show
that the torrent is no longer deliberately stopped.

## 7. Journal and interruption recovery

### 7.1 Purpose

The journal explains what QControl was doing across non-transactional HTTP
operations. It contains:

- schema version;
- activation identifier and start time;
- participating Jellyfin session identifiers;
- configuration snapshot and revision;
- qBittorrent endpoint identity without credentials;
- previous Alternative Limits state and whether QControl changed it;
- Marker and Never-touch Tag names used by the activation;
- affected hashes and per-hash operation progress;
- release state and last successful reconciliation;
- failure details safe for administrator display.

It does not contain credentials, authorization headers, usernames, media
titles, torrent names, tracker URLs, or file paths.

### 7.2 Location and durability

The journal lives at a runtime-resolved path equivalent to:

```text
<PluginConfigurationsPath>/Jellyfin.Plugin.QControl.journal.json
```

In the official Docker layout this is normally beneath
`/config/plugins/configurations`. It is separate from the normal plugin XML
configuration and is written using temporary-file, flush, and same-directory
atomic replacement.

### 7.3 Interrupted activation

Only the uninterrupted QControl process that began an activation may restore it
automatically. If the process restarts with unfinished journal state, QControl
does not start torrents or change Alternative Limits merely because Jellyfin
currently reports no playback.

If playback is present after restart, QControl may continue enforcing configured
protection, but automatic release remains blocked until the interruption is
resolved by an administrator.

The status page offers:

- **Resume marked torrents** — start non-excluded torrents carrying the Marker
  Tag and remove that tag after accepted starts.
- **Restore previous speed setting** — restore Alternative Limits only when the
  journal contains a known previous state.
- **Mark resolved without changing qBittorrent** — clear QControl's recovery
  record and leave current torrent and speed states unchanged.

Marked torrents without a journal are accepted as administrator state, not
reported as a collision. They remain visible in status and can be resumed
through the same explicit action.

### 7.4 Partial failure

qBittorrent mutations are not transactional. A failure:

1. Keeps successful earlier operations.
2. Retains journal progress.
3. Stops advancing the failed batch.
4. Exposes a bounded, redacted error.
5. Retries during later reconciliation when the activation is still live.

QControl never clears the Marker Tag before a corresponding start is accepted
and never clears its journal while an enabled action remains unresolved.

## 8. Configuration

V1 configuration contains:

| Area | Setting |
|---|---|
| Connection | qBittorrent base URL |
| Authentication | stored API key or secret-file path |
| Action | enable Alternative Limits |
| Action | enable Stop Torrents |
| Scope | all torrents or selected categories |
| Lifecycle | include incomplete torrents |
| Lifecycle | include completed torrents |
| Tags | Marker Tag |
| Tags | Never-touch Tag |
| Timing | release grace in seconds |
| Metadata | schema version and accepted revision |

Default behavior configuration is inert:

```text
Alternative Limits: disabled
Stop Torrents: disabled
Scope: all torrents
Include incomplete: enabled
Include completed: enabled
Marker Tag: jfStopped
Never-touch Tag: jfNeverTouch
Release grace: 60 seconds
```

Configuration validation occurs server-side. The embedded administrator page
does not duplicate policy rules in JavaScript. A valid connection test verifies
authentication and compatible application/Web API versions without changing
qBittorrent.

## 9. Credential handling

V1 uses qBittorrent's bearer API key. It supports two sources:

### 9.1 Stored key

The key is persisted with plugin configuration and protected by the host's
Jellyfin configuration-directory permissions. QControl does not claim portable
at-rest encryption where the platform provides no protected master key.

### 9.2 Secret file

The configuration stores a path and reads the key from that file. This works
with a Windows ACL-protected file, a native Unix file, a Docker secret, or
another read-only mounted secret. The file contents are never copied to the
journal or returned to the browser.

### 9.3 Dashboard and diagnostics

- A saved key is represented only as configured/not configured.
- A blank replacement field retains the current stored value.
- Replacing and clearing credentials are explicit actions.
- Connection-test responses never echo a credential.
- Authorization headers and strings beginning with `qbt_` are redacted.
- Credentials embedded in the configured URL are rejected.

## 10. Status and administrator experience

The status page reports:

- qBittorrent connectivity, application version, and Web API version;
- protection state: inactive, protecting, release pending, restoring, or
  recovery required;
- qualifying Jellyfin session count without usernames or media titles;
- enabled action state and Alternative Limits ownership;
- eligible, marked, stopped, and excluded torrent counts;
- release-grace countdown;
- whether saved configuration changes are waiting for the next activation;
- last successful reconciliation;
- current bounded error;
- manual recovery actions when applicable.

Configuration discovers available qBittorrent categories after a successful
connection test. Administrators may still retain configured categories that are
temporarily absent.

All QControl API endpoints require Jellyfin administrator authorization.

## 11. Architecture

### 11.1 Layers

```text
Jellyfin events + configuration + periodic timer
                     │
                     ▼
        Serialized reconciliation coordinator
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
  Pure activation/policy   Durable journal
       planning                 │
          │                     │
          └──────────┬──────────┘
                     ▼
            qBittorrent adapter
```

### 11.2 Pure domain layer

Responsibilities:

- derive playback presence from neutral session snapshots;
- reduce activation state with a fakeable clock;
- select eligible torrents from neutral torrent snapshots;
- plan Alternative Limits and explicit-hash torrent mutations;
- enforce tag precedence and lifecycle rules;
- produce deterministic, idempotent desired operations.

It performs no filesystem, HTTP, timer, or Jellyfin calls.

### 11.3 Application layer

Responsibilities:

- serialize wake-ups and periodic work;
- capture activation configuration;
- orchestrate read-plan-journal-apply-confirm passes;
- manage release grace and qBittorrent retries;
- retain privacy-safe status;
- execute explicit recovery commands.

### 11.4 Adapters

Responsibilities:

- observe Jellyfin sessions and events;
- authenticate and call the narrow qBittorrent Web API allowlist;
- atomically persist the journal;
- load and validate plugin configuration;
- expose administrator-only configuration and status endpoints.

### 11.5 Suggested source layout

```text
Jellyfin.Plugin.QControl/
├── Plugin.cs
├── ServiceRegistrator.cs
├── Configuration/
│   ├── PluginConfiguration.cs
│   └── configPage.html
├── Domain/
│   ├── PlaybackPresence.cs
│   ├── ProtectionActivation.cs
│   ├── TorrentSelection.cs
│   └── ProtectionPlan.cs
├── Application/
│   ├── ReconciliationCoordinator.cs
│   ├── ProtectionWorker.cs
│   ├── RecoveryService.cs
│   └── OperationalStatus.cs
├── Jellyfin/
│   └── SessionObserver.cs
├── QBittorrent/
│   ├── QBittorrentClient.cs
│   ├── CredentialProvider.cs
│   └── Contracts/
├── Persistence/
│   └── ActivationJournalStore.cs
└── Api/
    └── QControlController.cs
```

The tree is guidance, not a fixed class-level design.

## 12. Security and operational boundaries

- The qBittorrent key is effectively administrative and is used only through a
  hardcoded endpoint allowlist.
- QControl never adds, deletes, moves, renames, or rechecks torrent content.
- QControl never changes qBittorrent application preferences other than the
  selected Alternative Limits mode.
- QControl does not manipulate the Jellyfin database directly.
- All HTTP calls use bounded timeouts and cancellation.
- Normal logs contain counts, hashes only when needed for administrator
  diagnosis, and redacted errors; they exclude media and torrent names.
- The plugin remains inert on invalid configuration, failed authentication, or
  incompatible qBittorrent versions.
- Network or qBittorrent failure never implies absence of Jellyfin playback.

## 13. Architectural invariants

1. **Session-set truth:** events wake reconciliation; current sessions decide
   playback presence.
2. **Paused qualifies:** media open in a paused player remains presence.
3. **Serialized mutations:** one coordinator owns every qBittorrent mutation.
4. **Immediate protection:** presence starts protection without an activation
   grace.
5. **Graceful release:** restoration requires an empty presence set for the
   complete release grace.
6. **Independent actions:** Alternative Limits and Stop Torrents do not depend
   on each other.
7. **Explicit hashes:** no blind `stop all` or `start all` call is permitted.
8. **Tag authority:** the configurable Marker Tag expresses restart intent,
   including assignments predating the current installation.
9. **Exclusion wins:** QControl never mutates a torrent carrying the configured
   Never-touch Tag.
10. **No admin inference:** eligible torrents that restart during protection are
    stopped again.
11. **Category preservation:** QControl never changes torrent categories.
12. **Write ahead:** intended external mutations are journaled before issuance.
13. **Conservative interruption:** a new process does not automatically release
    an interrupted activation.
14. **Configuration stability:** one activation uses one behavior snapshot.
15. **Secret containment:** credentials never enter journals, status payloads,
    browser reads, or logs.

## 14. V1 completion criteria

### Behavior

- Any playing or paused Jellyfin player activates protection.
- Overlapping players maintain one activation until all close and grace expires.
- Alternative Limits, Stop Torrents, and both together behave independently.
- All/category and incomplete/completed selections produce exact expected
  hashes.
- New, queued, stalled, and manually restarted eligible torrents settle stopped.
- Normal release starts marked non-excluded torrents and preserves unmarked
  stopped torrents.

### Recovery

- Every journal mutation boundary is crash-tested.
- Restart never performs automatic release.
- Manual recovery actions are explicit, bounded, and idempotent.
- qBittorrent outages retain activation and recovery state.

### Administration

- Connection testing validates authentication and compatibility without writes.
- Configuration and status are administrator-only.
- Stored and file-based credentials work on Windows and Linux paths.
- Status explains which condition is keeping protection active.

### Distribution

- Clean build, unit, contract, and container integration tests pass.
- The plugin loads on its declared Jellyfin 10.11 target.
- A release ZIP, manifest, checksum, clean-install test, and upgrade contract are
  documented and reproducible.
