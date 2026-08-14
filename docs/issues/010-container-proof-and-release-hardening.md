# Issue 010 — Container proof and release hardening

## Behavior

Prove the complete V1 behavior against isolated real Jellyfin and qBittorrent
services, harden package/security/version contracts, and prepare—but do not
automatically publish—the first public release.

## Examples

- Playback presence activates both actions, queue promotion settles, and normal
  release restores marked state after grace.
- Killing Jellyfin at every required journal boundary produces recovery-required
  state and no automatic release after restart.
- A qBittorrent outage during acquisition or restoration recovers without
  losing journal ownership.
- A clean package installs through a temporary custom manifest and retains
  configuration across restart.

## ADRs

- ADR-0001 through ADR-0005

## TDD sequence

1. Add failing end-to-end scenarios for each enabled action and their
   combination.
2. Add generated local torrent fixtures and deterministic queue behavior.
3. Automate required crash points and qBittorrent outage injection.
4. Add package-content, version-drift, checksum, and manifest contract tests.
5. Add clean-install and restart tests.
6. Add the release workflow only after local contracts are green.

## Acceptance tests

- All test layers and behavior matrix rows in `TESTING.md` pass.
- Every required crash point has named automated or reproducible evidence.
- No integration test contacts a public tracker, media server, or operator
  service.
- Package loads on its declared Jellyfin 10.11 ABI and rejects unsupported
  qBittorrent versions safely.
- Windows stored/file credential smoke instructions are documented and either
  automated in CI or explicitly reported as a remaining platform limitation.
- Release artifacts, manifest entry, immutable URL, and checksums agree.
- No push, GitHub repository creation, issue filing, or release occurs without a
  separate explicit instruction.

## Out of scope

- qBittorrent 4.x.
- Multiple qBittorrent instances.
- Official Jellyfin catalog submission.
- Adaptive disk/bandwidth control or VPN validation.
