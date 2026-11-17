# Jellyfin 12 migration

This branch targets Jellyfin packages `12.0.0`, plugin ABI `12.0.0.0`, and
`net10.0`. The SDK minimum is `10.0.100` with `latestMinor` roll-forward.
Initial smoke tests used an unpublished `0.1.0.1` build. The distinct release
candidate is now `0.2.0.0`; its versioned package and release contracts must
pass before publishing.

The previous source is preserved at
[`legacy/jellyfin-10.11`](https://github.com/darvilp/jf-QControl/tree/legacy/jellyfin-10.11),
commit `7dfb0869ad6c36bd0541521c0f48335f0fb0cc36`. Existing catalog entries and
release ZIPs remain the published 10.11 builds. Do not install those on 12.

## Verified requirements

- The [official 12.0 announcement](https://jellyfin.org/posts/jellyfin-release-12.0/)
  explains that 10.12 became 12.0, with server version `12.0.0`, and requires
  rebuilt plugins.
- [Jellyfin.Controller 12.0.0 on NuGet](https://www.nuget.org/packages/Jellyfin.Controller/12.0.0)
  targets .NET 10. The Common, Controller, and Model package versions are pinned
  together at the first stable 12 release. Later 12.x packages exist, but this
  migration uses the baseline rather than raising the minimum server version.
- The [12.0 session interface](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Session/ISessionManager.cs)
  retains the playback and session events and session enumeration used by
  QControl. The plugin, adapters, DI registration, administrator controllers,
  and tests compile against the actual packages without API substitutions.
- The isolated fixture uses the official `jellyfin/jellyfin:12.0` image, pinned
  to registry digest `sha256:baba630419915985442f315f08b0cf46d9f4c8a0cc4bd38e94a6d35751dd5ef5`.
  Release validation normalizes its two-component image tag to `12.0.0`.

## Cloud verification

- Release solution build passed with SDK `10.0.404`, zero warnings and errors.
- All 174 .NET tests passed, including plugin identity, embedded resources,
  session contracts, observer lifecycle, configuration, authorization,
  coordination, recovery, and qBittorrent HTTP behavior.
- JavaScript syntax check and all 18 dashboard tests passed on Node `24.22.1`.
  The obsolete module flag was removed because package.json already declares
  ES modules.
- Static Compose environment and release-tooling checks passed. The latter
  accepts `12.0` and `12.0.0` image tags and rejects a mismatched `12.1` target.

- JPRM packaging and the package contract passed. The ZIP contains only the
  plugin DLL, domain DLL, and metadata, with the unchanged GUID and new ABI.
  Local ZIP SHA-256: `c995a897da38fb917ec555351ae4854454655fa176aecc7e5e01a452dc646cff`.
- ShellCheck `0.11.0` and `git diff --check` passed.
- No checks remain failed. Initial failures from the obsolete Node flag and
  two-component image version were corrected and their checks rerun.
  Package tooling emitted its existing missing-image warning; the test runner
  emitted a temporary-artifact-directory fallback notice. Both exited zero.

These are compile and automated contract results. They do not establish that
Jellyfin loads the packaged plugin or that a real player drives protection.
The historical 10.11 compatibility reports remain historical evidence.

## Local runtime handoff

Use a separate checkout and the repository-owned `.testenv` fixtures on
TheMachine. Do not point these checks at the live server. Start with fresh
fixture state for 12; retain any 10.11 configuration/data backup separately.

1. Run `scripts/dotnet.sh restore Jellyfin.Plugin.QControl.sln`,
   `scripts/dotnet.sh test Jellyfin.Plugin.QControl.sln --configuration Release`,
   and `tests/packaging/package-contract.test.sh` with .NET 10 available.
2. Run the issue gates from the README against the pinned 12.0 fixture. Confirm
   plugin discovery and Active status, unchanged GUID, configuration loading,
   and no assembly-load or DI errors in Jellyfin logs.
3. Check administrator dashboard resources, connection validation, stored-key,
   file-key and bypass modes, and rejection of non-administrator requests.
4. Exercise playing, paused, overlapping, stopped, and disconnected sessions.
   Confirm grace expiry and exact restoration for both Alternative Limits and
   Stop Torrents, including multiple exclusion tags.
5. Run interruption/outage and browser gates, then the temporary-manifest
   installation check. Verify recovery after server restart and qBittorrent
   unavailability, with no unwanted changes to pre-existing torrent state.
6. Record server version, image digest, commands, results, and logs in this
   document. Check a later 12.x server separately before claiming that range.

The initial migration did not run runtime tests. The cloud follow-up below
records the subsequent smoke tests. Browser E2E, interruption/outage, and
temporary-manifest installation remain separate checks.
Review these results before merging. Release tagging, publication, catalog
updates, and deployment are separate maintainer actions.

## Disposable cloud runtime follow-up

On 2026-11-17, the same migration was rebuilt successfully with zero warnings
and errors, then installed in the pinned Jellyfin 12.0.0 Docker image. The
original migration CI run also passed:
[GitHub Actions 44026251172](https://github.com/darvilp/jf-QControl/actions/runs/44026251172).

The first authenticated setup request exposed a fixture migration defect.
Login succeeded, but requests with `X-Emby-Token` returned HTTP 401. The fixture
scripts and browser API helpers now send `Authorization: MediaBrowser
Token="..."`. The plugin dashboard itself uses Jellyfin's ApiClient.

`tests/fixtures/jellyfin-compatibility.test.sh` passed after that fix. It built
and verified the ZIP, installed it in the isolated plugin directory, restarted
Jellyfin, and confirmed QControl `0.1.0.1` was Active with the original GUID.
It also checked inert default configuration, saved configuration revision 1,
restarted again, verified configuration and journal-file persistence, and
retrieved the embedded administrator page. Server logs reported
`Loaded plugin: QControl 0.1.0.1`; its status API returned Inactive with both
actions disabled before configuration.

The cloud needed temporary fixture adaptations, which are not production
changes. `lscr.io` returned HTTP 403, so the test pulled
`docker.io/linuxserver/qbittorrent` at the identical pinned digest. Injected
proxy settings were cleared inside the four fixture containers, and wget
health checks used `-Y off`. Synthetic webseed directories and files received
755/644 permissions so nginx could serve them. These changes did not affect
secrets or live data. The normal compose file was restored after testing.

An initial standalone Jellyfin attempt was healthy inside its container but
unreachable through a published port on the internal-only network. Testing
then used the repository's normal gateway and isolated network layout.
External plugin-catalog requests failed on that deliberately isolated network;
local plugin installation did not depend on the external catalog.

`tests/fixtures/jellyfin-qcontrol-contract.test.sh` also passed against real
Jellyfin 12.0.0 and qBittorrent 5.2.3. It verified unauthenticated HTTP 401 and
non-administrator HTTP 403 responses, connection validation including trusted
subnet bypass and secret-file credentials, configuration and category APIs,
and credential-free responses and journaling. Synthetic playback reports
activated both Alternative Limits and selected-torrent stops. Stopping
playback restored the original limits and torrent state without changing
categories. Explicit marked-torrent recovery also completed successfully.

Both runtime test scripts removed their fixture containers and networks on
exit. No live server, live torrent, published release, or catalog was touched.
ShellCheck, browser-test JavaScript syntax, and diff whitespace checks passed
after the authorization fix. No runtime smoke failures remain unresolved.

This is a server/API smoke test, not a real-player or browser E2E result.
Real-player pause/disconnect behavior, interruption/outage recovery,
temporary-manifest catalog installation, upgrades from an existing 10.11
configuration, and later 12.x releases remain untested in this follow-up.
