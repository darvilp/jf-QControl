---
status: accepted
---

# Support explicit qBittorrent authentication bypass

QControl will support three explicit qBittorrent authentication modes: an API
key stored in Jellyfin configuration, an API key read from a secret file, and
no authentication header. The third mode is for qBittorrent installations that
already bypass Web UI authentication for localhost or for a whitelisted client
subnet.

Unauthenticated mode is never inferred from a missing or invalid API key. The
administrator must select it, and QControl must pass the same read-only
connection test required by the authenticated modes before any protection
action can run. Switching authentication modes invalidates prior connection
validation and is prohibited during an active protection activation.

## Consequences

- QControl sends no `Authorization` header in unauthenticated mode; endpoint
  allowlisting, timeouts, version checks, and response validation are unchanged.
- QControl does not enable or broaden qBittorrent's authentication bypass. The
  administrator remains responsible for limiting it to Jellyfin's source IP or
  another appropriately trusted subnet.
- `localhost` describes the machine or network namespace running qBittorrent.
  Separate Jellyfin and qBittorrent containers normally require a narrowly
  scoped subnet-whitelist entry instead of qBittorrent's localhost option.
- An existing stored key is retained when another mode is selected unless the
  administrator explicitly clears it, consistent with other mode switches.
- Authentication failures use mode-neutral language because rejection may mean
  an invalid key or a source address outside qBittorrent's bypass rules.

This decision supersedes ADR 0005's API-key-only requirement while retaining
its qBittorrent 5.2 minimum and rejection of username/password login flows.
