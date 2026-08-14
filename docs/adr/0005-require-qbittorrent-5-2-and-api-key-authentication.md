---
status: accepted
---

# Require qBittorrent 5.2 and API-key authentication

V1 will require qBittorrent 5.2 or newer and authenticate to one Web API endpoint
with its bearer API key. This gives QControl stateless authentication and the
deterministic Alternative Limits setter without carrying legacy pause/resume or
cookie-login branches. The administrator may store the key in Jellyfin plugin
configuration or provide a readable secret-file path on Windows, Linux, or in a
container.

## Consequences

- Username/password authentication and qBittorrent 4.x are outside V1.
- The qBittorrent key remains broadly privileged because qBittorrent does not
  offer scoped API keys; QControl uses a narrow internal endpoint allowlist.
- QControl never returns a stored key to the browser or records it in logs,
  status, or the journal.
- Stored configuration is protected by Jellyfin host filesystem permissions;
  a secret file is available when stronger separation is required.
- HTTP is supported for trusted LAN/container networks. HTTPS uses normal
  certificate validation; QControl provides no ignore-certificate-errors mode.
