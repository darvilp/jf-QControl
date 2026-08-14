---
status: accepted
---

# Journal active work and require manual interruption recovery

Before mutating qBittorrent, QControl will atomically persist a small journal in
Jellyfin's plugin-configuration directory. The journal records the activation,
configuration snapshot, previous Alternative Limits state, whether QControl
changed it, affected hashes, and operation progress. The Marker Tag remains the
qBittorrent-side authority for torrent control; the journal explains the
activation and makes partial operations diagnosable.

Only the uninterrupted process that began an activation may release it
automatically. After a Jellyfin or plugin interruption, QControl leaves current
qBittorrent state unchanged and offers explicit administrator actions:
“Resume marked torrents,” “Restore previous speed setting” when known, and
“Mark resolved without changing qBittorrent.” This favors continued playback
protection over speculative crash recovery.

## Consequences

- The journal lives beside plugin configuration, survives normal container and
  server restarts, and never contains credentials, usernames, media titles, or
  torrent names.
- Writes use atomic replacement so a partial JSON document is never accepted.
- qBittorrent unavailability retains recovery state and retries; it never
  silently advances operation progress.
- Successful release removes the Marker Tag only after a start succeeds and
  clears the active journal only after all selected actions settle.
