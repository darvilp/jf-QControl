---
status: accepted
---

# Reconcile playback presence through one coordinator

Jellyfin playback events will only wake one serialized reconciliation
coordinator. Each reconciliation rereads current Jellyfin sessions; any session
with media open counts as playback presence, including a paused player. A
protection activation starts immediately when presence appears and enters a
configurable release grace when the set becomes empty. This desired-state model
handles duplicate, missing, reordered, and overlapping session events without
translating individual events into blind qBittorrent actions.

## Consequences

- Multiple sessions aggregate into one protection activation.
- Pausing does not begin release; a player must stop or disappear.
- Playback returning during release grace cancels release immediately.
- The default release grace is 60 seconds.
- While active, qBittorrent is reconciled every 5 seconds; connection failures
  retry every 15 seconds.
- Ordinary configuration changes are convenient to save but take effect at the
  next activation through a fixed configuration snapshot. Credential changes
  may reconnect immediately.
