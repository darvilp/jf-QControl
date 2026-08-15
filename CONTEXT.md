# QControl

QControl coordinates Jellyfin playback presence with temporary qBittorrent
protection. Its language separates what Jellyfin observes, what qBittorrent is
asked to do, and what the plugin is allowed to restore.

## Language

**Playback presence**:
A Jellyfin session with media open in its player, whether currently advancing
or paused.
_Avoid_: Active stream, unpaused playback

**Protection activation**:
One uninterrupted period beginning when playback presence first appears and
ending after playback presence remains absent for the release grace.
_Avoid_: Jellyfin session, lease

**Release grace**:
The configured period for which QControl waits after playback presence
disappears before ending a protection activation.
_Avoid_: Pause timeout, activation delay

**Protection action**:
An independently enabled qBittorrent behavior applied during a protection
activation. V1 actions are Alternative Limits and Stop Torrents.
_Avoid_: Mode

**Alternative Limits**:
The qBittorrent global alternative-speed state that QControl can enforce during
a protection activation.
_Avoid_: Throttle value, alternate rates

**Stop scope**:
The administrator-selected torrent population considered by Stop Torrents:
either every torrent or torrents in selected qBittorrent categories.
_Avoid_: Pause category, global pause

**Lifecycle selection**:
The choice to include incomplete torrents, completed torrents, or both in the
stop scope.
_Avoid_: Torrent state filter

**Eligible torrent**:
A non-stopped torrent inside the stop scope and lifecycle selection that does
not carry any configured Exclusion Tag.
_Avoid_: Running torrent, active torrent

**Marker Tag**:
The configurable qBittorrent tag expressing administrator intent that QControl
may keep a torrent stopped and later start it.
_Avoid_: Temporary category, ownership record

**Exclusion Tag**:
A qBittorrent tag whose presence excludes a torrent from every QControl torrent
mutation. Any Exclusion Tag takes precedence over the Marker Tag.
_Avoid_: Never-touch Tag, ignore category, manual override

**Exclusion Tag List**:
The possibly empty administrator-configured set of exact Exclusion Tags; a
match on any member excludes the torrent.
_Avoid_: Never-start tags, tag filter

**Configuration snapshot**:
The QControl settings fixed for one protection activation so ordinary
configuration changes cannot alter its meaning partway through.
_Avoid_: Pending configuration

**Journal**:
The durable record explaining an in-progress protection activation, its
configuration snapshot, its qBittorrent mutations, and its recovery state.
_Avoid_: Log, ownership database

**Interrupted activation**:
A protection activation whose originating QControl process ended before a
normal release completed.
_Avoid_: Failed session, stale journal

**Manual recovery**:
An explicit administrator action that resolves marked torrents or speed state
after an interrupted activation.
_Avoid_: Crash recovery

**Reconciliation**:
A serialized comparison of current playback presence, the current protection
activation, and observed qBittorrent state that applies only the mutations
needed by the selected actions.
_Avoid_: Poll cycle, event handler
