---
status: accepted
---

# Run as an in-process Jellyfin plugin

QControl will run as one in-process Jellyfin server plugin and control one
qBittorrent instance directly through its Web API. Direct Jellyfin session
events provide low-latency wake-ups and the current session collection provides
authoritative playback presence without a Jellyfin API credential, webhook
receiver, broker, or companion service. This couples releases to the Jellyfin
plugin ABI and means QControl cannot recover while Jellyfin is down; the
conservative recovery model in ADR-0004 accepts that limitation in exchange for
one installation and configuration surface.

## Considered options

- A webhook-assisted external service could survive Jellyfin crashes but would
  require another deployable, a Jellyfin credential, and duplicate operational
  configuration.
- A message broker would add no useful durability beyond an external
  reconciler for this single-server product.

## Consequences

- V1 supports one Jellyfin server and one qBittorrent instance.
- Jellyfin package and runtime compatibility must be tested explicitly.
- Blocking network work cannot run on Jellyfin event-handler threads.
