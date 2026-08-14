# QControl — Testing Strategy

## 1. TDD standard

Every behavioral issue follows:

```text
red → verify the intended failure → green → refactor → full suite
```

Tests describe observable protection semantics rather than mirror proposed
classes. A production behavior is not complete because it compiles or because a
mock verifies one method call.

Each issue records:

- the first failing test and why it failed;
- targeted test commands and results;
- the full required suite and result;
- integration or package evidence when the behavior crosses a real boundary.

## 2. Test layers

### 2.1 Pure domain tests

No Jellyfin assemblies, HTTP, filesystem, or real clock.

Cover:

- playback-presence derivation;
- activation and release-grace transitions;
- overlapping session aggregation;
- torrent scope and lifecycle selection;
- Marker and Never-touch Tag semantics;
- explicit-hash mutation planning;
- Alternative Limits ownership planning;
- configuration-snapshot behavior;
- idempotence and deterministic plans.

### 2.2 Application-service tests

Use fake clocks, session sources, qBittorrent ports, journal stores, and
credential sources.

Cover:

- serialized event/timer wake-ups;
- periodic reconciliation;
- cancellation and shutdown;
- read-plan-journal-apply-confirm ordering;
- retry without false release;
- partial batch progress;
- interrupted activation behavior;
- manual recovery commands;
- privacy-safe operational status.

### 2.3 Persistence tests

Use an isolated temporary directory.

Cover:

- journal round-trip and schema version;
- atomic replacement;
- absent, valid, truncated, and incompatible journals;
- failure before and after replacement;
- plugin-configuration and journal separation;
- no credential or media/torrent display data in journal JSON.

### 2.4 qBittorrent HTTP contract tests

Run first against a deterministic in-process HTTP stub and then against the
pinned qBittorrent container.

Cover:

- bearer authorization on every allowed request;
- application and Web API version negotiation;
- deterministic Alternative Limits get/set;
- torrent/category reads;
- add/remove tag;
- start/stop explicit hash batches;
- malformed JSON and non-success responses;
- timeouts, cancellation, and lost connections;
- redaction of headers, API keys, URLs, and response errors;
- absence of calls to destructive or non-allowlisted endpoints.

### 2.5 Jellyfin contract tests

Cover:

- minimal plugin load on the selected Jellyfin 10.11 ABI;
- start, progress, stop, and session lifecycle event behavior;
- playing and paused `SessionInfo` shapes;
- current-session enumeration after events;
- plugin configuration serialization;
- runtime plugin-configuration path resolution;
- administrator authorization on custom endpoints;
- embedded configuration-page discovery.

### 2.6 Container integration tests

Reuse the isolated Docker conventions established by `jf-TagSync`:

- pinned ephemeral Jellyfin and qBittorrent services;
- isolated configuration/data volumes;
- deterministic readiness probes;
- generated local torrent fixtures with no public tracker dependency;
- captured logs on failure;
- explicit project/container names;
- teardown incapable of touching operator services.

The environment uses one controlled qBittorrent service, a local throttled HTTP
web seed, and an optional second local peer used only to prove genuine upload
behavior. Small deterministic trackerless torrents provide completed,
incomplete, queued, stalled, stopped, categorized, marked, and excluded
fixtures. DHT, PeX, LPD, UPnP, public trackers, and host torrent ports are
disabled.

Real-container tests prove stable representative states. Transient qBittorrent
states are covered exhaustively through neutral domain snapshots and HTTP
contracts unless the compatibility spike finds materially different real stop
behavior. Each real fixture is polled to a bounded precondition; fixed sleeps do
not establish status.

The integration suite proves behavior across both real APIs; it does not use or
modify an administrator's existing Jellyfin or qBittorrent installation.

### 2.7 Release smoke tests

Cover:

- clean ZIP installation;
- custom manifest installation;
- plugin load and configuration retention;
- journal location persistence across container recreation;
- package contents and checksums;
- version consistency across assembly, build metadata, package, release tag,
  and manifest;
- upgrade from the previous public package beginning with the second release.

## 3. High-value properties

### Idempotence

Reconciliation against already-protected or already-restored state emits no new
mutation.

### Exclusion dominance

Adding the Never-touch Tag removes a torrent from every planned torrent
mutation, even when it also carries the Marker Tag.

### Unmarked-stop preservation

A torrent stopped without the Marker Tag is never started by normal release.

### Explicit-hash closure

Every hash passed to stop was successfully included in the preceding marker
operation for that batch. No request uses the special `all` value.

### Playback aggregation

Protection release is impossible while any session has media open, regardless
of that session's paused state.

### Grace cancellation

Playback presence at any instant before release-grace expiry returns the same
activation to protecting without a restore operation.

### Configuration stability

For one activation, changing saved behavior configuration does not change its
selected actions, tags, scope, lifecycle, or grace.

### Alternative Limits ownership

Normal release disables Alternative Limits if and only if the uninterrupted
activation changed it from disabled to enabled.

### Interruption safety

Loading unfinished journal state in a new process never plans an automatic
start or Alternative Limits restore.

### Secret non-observability

No configuration read, status response, journal, exception, or log event
contains the API key or secret-file contents.

## 4. Core behavior matrix

| Area | Required case |
|---|---|
| Playback | No sessions means no playback presence |
| Playback | Playing session qualifies |
| Playback | Paused session qualifies |
| Playback | Session without current media does not qualify |
| Playback | Two sessions aggregate into one activation |
| Playback | One of two sessions ending does not begin release |
| Playback | Last session ending begins release grace |
| Playback | Presence returning during grace cancels release |
| Playback | Exact grace boundary releases once |
| Playback | Duplicate and reordered events only cause rereads |
| Playback | Missed start event is repaired by inactive periodic reread |
| Configuration | New installation is inert |
| Configuration | Invalid connection cannot enable mutation |
| Configuration | Active configuration snapshot remains stable after save |
| Configuration | Replacement credential reconnects immediately |
| Configuration | Marker and never-touch names must be distinct and non-empty |
| Scope | All includes categorized torrents |
| Scope | All includes uncategorized torrents |
| Scope | Selected categories use exact names |
| Scope | Unselected categories remain untouched |
| Lifecycle | Incomplete-only includes remaining-content torrents |
| Lifecycle | Completed-only includes zero-remaining torrents |
| Lifecycle | Both includes both populations |
| Lifecycle | Transient queue/stall state does not change completion class |
| Exclusion | Never-touch excludes an otherwise eligible torrent |
| Exclusion | Never-touch wins when both tags are present |
| Acquisition | Already-stopped unmarked torrent remains unmarked |
| Acquisition | Marker is persisted before stop request |
| Acquisition | Initial pass includes queued and stalled eligible torrents |
| Acquisition | New eligible torrent is acquired on a later pass |
| Acquisition | Manually restarted eligible torrent is stopped again |
| Acquisition | Category never changes |
| Acquisition | No stop request uses `all` |
| Restoration | Marked non-excluded torrent is started |
| Restoration | Marker is removed only after read-back confirms not stopped |
| Restoration | Unmarked stopped torrent remains stopped |
| Restoration | Pre-existing Marker Tag is accepted as restart intent |
| Restoration | Never-touch marked torrent receives no start or tag mutation |
| Restoration | Queued-after-start counts as accepted restoration |
| Restoration | No start request uses `all` |
| Alternative Limits | Initially disabled is enabled and owned |
| Alternative Limits | Initially enabled remains enabled and unowned |
| Alternative Limits | Manual disable during protection is re-enabled |
| Alternative Limits | Owned transition is disabled on normal release |
| Alternative Limits | Unowned initial state remains enabled on release |
| Combined | Stop and Alternative Limits can acquire together |
| Combined | One action failure retains the other action's known state |
| Journal | Intent is durable before each external mutation |
| Journal | Successful confirmations advance operation progress |
| Journal | Truncated write never replaces last valid journal |
| Journal | Journal contains no credentials or display names |
| Interruption | New process does not automatically restore |
| Interruption | Playback after restart may continue protection |
| Interruption | Recovery-required state blocks automatic release |
| Recovery | Resume marked torrents is explicit and idempotent |
| Recovery | Previous speed setting is available only when known |
| Recovery | Mark resolved changes no qBittorrent state |
| Failure | qBittorrent outage never means playback absent |
| Failure | Retry does not duplicate effective mutations |
| Failure | Partial tag batch does not stop unmarked hashes |
| Failure | Partial start retains Marker Tag on unresolved hashes |
| Security | API key uses bearer header, never query string |
| Security | URL user-info is rejected |
| Security | TLS certificate failures are not bypassed |
| Security | Custom APIs reject non-administrators |
| Status | No usernames, media titles, torrent names, or secrets are returned |
| Platform | Stored key path works under Windows Jellyfin configuration |
| Platform | Secret-file source accepts Windows and Unix paths |

## 5. Required crash points

Fault-inject every boundary:

1. Before activation journal creation.
2. After journal creation, before reading qBittorrent.
3. After selected hashes are journaled, before adding the Marker Tag.
4. After partial marker success.
5. After marker success, before stop.
6. After partial stop.
7. After all stops, before journal confirmation.
8. During release grace.
9. After restart intent is journaled.
10. After partial start.
11. After start, before marker removal.
12. After partial marker removal.
13. After action settlement, before journal cleanup.

For every point assert that a new process performs no automatic release, status
explains recovery is required, and the explicit recovery operations remain
idempotent.

## 6. Integration spike checklist

Before production adapters are trusted, record exact results in
`docs/compatibility/<jellyfin>-<qbittorrent>.md`:

1. Minimal package loads on the selected Jellyfin release.
2. Jellyfin event subscriptions and current-session snapshots match source
   research for playing, paused, stopped, disconnected, and overlapping clients.
3. Plugin configuration and journal paths persist across server restart.
4. qBittorrent accepts API-key authentication from Jellyfin's HTTP client.
5. Version endpoints return the expected application and Web API versions.
6. Deterministic Alternative Limits setter reaches the requested state.
7. Torrent listing exposes hash, category, tags, remaining content, and stopped
   state needed by the selector.
8. Batched tag, stop, start, and tag-removal operations behave as documented.
9. Queue promotion is reproduced with local fixtures.
10. An accepted start that remains queued is distinguishable from stopped.
11. Category and tag names with spaces and non-ASCII characters round-trip.
12. Stored and file-based keys work on Linux/container paths; Windows behavior
    has unit coverage and a documented native smoke procedure if CI lacks a
    Windows Jellyfin runtime.
13. API-key bootstrap uses only fixture credentials, writes an ephemeral secret
    file, and never exposes either credential in retained output.

## 7. Test evidence per issue

An implementation issue is complete only when its acceptance tests have:

- named tests proving the behavior;
- the observed red failure captured before implementation;
- targeted test command and passing result;
- full suite command and passing result;
- real-boundary evidence for any Jellyfin, qBittorrent, filesystem, UI, package,
  or platform claim it introduces.

Compilation alone is never runtime, API, recovery, or packaging proof.
