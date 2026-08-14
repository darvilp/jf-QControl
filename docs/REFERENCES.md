# References

Research was verified on 2026-08-13 and 2026-08-14. Exact compatibility results
will be pinned separately under `docs/compatibility/` during Issue 001.

## Jellyfin

- [Jellyfin 10.11.11 release](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11)
- [`ISessionManager` in Jellyfin 10.11.11](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Controller/Session/ISessionManager.cs#L24-L62)
- [Jellyfin plugin template](https://github.com/jellyfin/jellyfin-plugin-template)
- [Jellyfin plugin installation and configuration storage](https://jellyfin.org/docs/general/server/plugins/index.html)
- [Official Jellyfin container layout](https://jellyfin.org/docs/general/installation/container/)

## qBittorrent

- [qBittorrent 5.2.3 release](https://github.com/qbittorrent/qBittorrent/releases/tag/release-5.2.3)
- [qBittorrent 5 WebUI API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29)
- [API-key authentication for qBittorrent 5.2+](https://github.com/qbittorrent/qBittorrent/wiki/API-Key-Authentication-%28%E2%89%A5v5.2.0%29)
- [Deterministic Alternative Limits implementation in 5.2.3](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/api/transfercontroller.cpp#L90-L146)
- [Torrent start/stop implementation in 5.2.3](https://github.com/qbittorrent/qBittorrent/blob/0b63c3d17373f6132ea211c9dcd4241284ccdfaf/src/webui/api/torrentscontroller.cpp#L1429-L1446)

## Existing and adjacent projects

- [Speedrr](https://github.com/itschasa/speedrr)
- [Jellyfin qBittorrent Monitor](https://github.com/Aelzaire/Jellyfin-qBittorrent-Monitor)
- [jellyfin-qbittorrent](https://github.com/Zouizoui78/jellyfin-qbittorrent)
- [Jellyfin Webhook plugin](https://github.com/jellyfin/jellyfin-plugin-webhook)
- [Home Assistant Jellyfin integration](https://www.home-assistant.io/integrations/jellyfin/)
- [Home Assistant qBittorrent integration](https://www.home-assistant.io/integrations/qbittorrent/)

See [`research/first-pass.md`](research/first-pass.md) for the evaluated behavior,
limitations, and source-level citations behind the initial product decision.
