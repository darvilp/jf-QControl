# Release preparation

QControl uses four-component plugin versions such as `0.1.0.0` and matching
tags such as `v0.1.0.0`. The current alpha is prepared for testing; no public
release or catalog manifest has been published.

## Install an alpha test package

1. Confirm the Jellyfin server is `10.11.11`. Stop Jellyfin.
2. Create a `QControl` directory under Jellyfin's plugin directory.
3. Extract `Jellyfin.Plugin.QControl.dll` and
   `Jellyfin.Plugin.QControl.Domain.dll` from the candidate ZIP into that
   directory. `meta.json` describes the package and does not need to be copied
   for a manual install.
4. Start Jellyfin. Confirm **Dashboard > Plugins > My Plugins > QControl** shows
   version `0.1.0.0` as Active.
5. Open **Dashboard > Plugins > QControl**, configure the qBittorrent endpoint
   and one credential source, test the connection, select at least one action,
   and save.

Keep a copy of the exact ZIP and its `.sha256` companion. Alpha removal should
be performed with Jellyfin stopped. If a QControl recovery warning is present,
resolve or intentionally mark that journal resolved before removing the
plugin; otherwise qBittorrent may remain protected by design.

## qBittorrent credential files

Stored mode writes the API key into Jellyfin's plugin configuration using the
same storage protections as other Jellyfin configuration. Secret-file mode is
preferred when the deployment already has a secrets mechanism. The file must
contain only the qBittorrent API key with optional surrounding whitespace.

For a container, mount the file read-only and enter its path inside the
Jellyfin container, for example `/run/secrets/qbittorrent-api-key`. On native
Linux, make the Jellyfin service account the owner and remove group/other
access.

On Windows:

1. Determine the Windows account running the Jellyfin Server service in
   `services.msc`.
2. Create a file outside any web-served directory, for example
   `C:\ProgramData\Jellyfin\Server\secrets\qcontrol-qbittorrent-api-key.txt`.
3. Put only the API key in that file. In the file's Security properties,
   disable inherited access and grant Read only to the Jellyfin service account
   and Full control to the administrator maintaining it.
4. Select **Secret file** in QControl, enter the absolute Windows path, test the
   connection, save, and restart Jellyfin.
5. Reopen QControl and test the connection again. Confirm that the key is not
   shown in the page or returned by the configuration API.
6. Repeat once with **Stored API key** mode to smoke both credential paths, then
   retain whichever mode matches the deployment policy.

The path construction and platform-neutral file APIs have automated coverage,
but this Linux-hosted project does not claim a native Windows Jellyfin runtime
smoke for `0.1.0.0`. Record the Jellyfin service account, Windows/Jellyfin
versions, both connection-test outcomes, and restart-retention result when the
procedure is run. Do not record the API key or include it in screenshots.

## Candidate validation

The local preparation path is read-only with respect to GitHub:

```bash
artifact="$(scripts/package.sh | tail -n 1)"
scripts/verify-release-contract.sh v0.1.0.0 "${artifact}"
scripts/prepare-release-assets.sh v0.1.0.0 "${artifact}" artifacts/release
scripts/test-manifest-install.sh "${artifact}"
scripts/test-issue-010.sh
```

`verify-release-contract.sh` rejects drift among the tag, build metadata,
assembly versions, target framework, Jellyfin ABI packages and container,
package metadata, candidate manifest, immutable release URL, and package MD5.
`prepare-release-assets.sh` copies the verified bytes to the final asset name
and writes a SHA-256 companion.

## Maintainer release checklist

1. Run the complete Issue 010 gate from a clean commit and review the alpha
   limitations below.
2. Create the exact annotated tag only after explicit release authorization.
3. Push the tag. The release-candidate workflow validates the exact tagged
   commit and creates a **draft prerelease** with immutable candidate assets.
4. Download the draft assets, verify the SHA-256 file, and install through the
   candidate manifest on a clean Jellyfin instance.
5. Human-review the draft notes, compatibility record, and Windows evidence.
6. Publish the draft and publish/update a catalog manifest only as separate,
   explicit maintainer actions.

Manual workflow dispatch accepts an existing tag and performs validation only;
it cannot create a release. The workflow never publishes a draft and never
writes a catalog branch.

## Alpha limitations

- Jellyfin `10.11.11` and qBittorrent `5.2.3` are the only proven versions.
- qBittorrent 4.x, multiple qBittorrent instances, VPN/public-IP validation,
  adaptive throttling, and automatic crash restoration are out of scope.
- This first release has no prior public version, so upgrade testing begins
  with the second release.
- Native Windows credential smoke remains a documented operator test until a
  Windows Jellyfin CI/runtime fixture exists.
