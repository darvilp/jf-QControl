# Jellyfin 12 migration

This branch targets Jellyfin packages `12.0.0`, plugin ABI `12.0.0.0`, and
`net10.0`. The SDK minimum is `10.0.100` with `latestMinor` roll-forward.
The existing plugin version remains `0.1.0.1` for this unpublished migration.
A maintainer must assign a new version before any release.

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

Runtime/server, browser E2E, real qBittorrent mutation, interruption/outage,
and temporary-manifest installation tests were not run in the cloud migration.
Review these results before merging. Release tagging, publication, catalog
updates, and deployment are separate maintainer actions.
