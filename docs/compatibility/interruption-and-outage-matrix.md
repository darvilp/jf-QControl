# Interruption and outage evidence matrix

Status: **passed on 2026-08-14**

This matrix binds every crash point in [the testing strategy](../TESTING.md) to
a deterministic mutation-seam test. A single cross-cutting lifecycle test
proves that loading any unfinished journal from another process grants only
protect-only authority: absent playback cannot start torrents, restore
Alternative Limits, remove Marker Tags, or delete the journal. The mutation
tests below establish the exact durable document that can exist at each seam.

`jellyfin-interruption-contract.test.sh` supplements these injected seams with
a real `SIGKILL` of Jellyfin after both actions settle. After restart it proves
zero automatic restoration, `RecoveryRequired` status, preserved qBittorrent
protection, explicit speed/torrent recovery, Marker Tag cleanup, journal
cleanup, and unchanged categories.

| # | Required boundary | Named automated evidence |
|---:|---|---|
| 1 | Before activation journal creation | `PausedPlaybackCreatesJournalBeforeProtectionAction`; `InterruptedJournalNeverRestoresFromAnyActiveLifecyclePhase` |
| 2 | Journal created, before qBittorrent read | `PausedPlaybackCreatesJournalBeforeProtectionAction`; `ActionFailureDoesNotChangeAuthoritativePlaybackPresence` |
| 3 | Selected hashes journaled, before Marker Tag add | `ProtectionPersistsIntentBeforeTagAndStopMutations`; `FailedTagMutationLeavesDurableIntentAndRetryUsesReadback` |
| 4 | Partial Marker Tag success | `PartialTagBatchStopsOnlyReadbackConfirmedHashThenConverges` |
| 5 | Marker Tag success, before stop | `ProtectionPersistsIntentBeforeTagAndStopMutations` verifies marker confirmation precedes stop intent and request |
| 6 | Partial stop | `PartialStopBatchRetainsIntentForRunningHashThenConverges` |
| 7 | Stop accepted, before stop confirmation | `RetryConfirmsStopThatSucceededBeforeReadbackFailed` |
| 8 | During release grace | `OverlappingSessionsRequireFullGraceAndPresenceCancelsRelease`; `InterruptedJournalNeverRestoresFromAnyActiveLifecyclePhase(ReleasePending)` |
| 9 | Restart intent journaled | `RestorationConfirmsStartReadbackBeforeRemovingMarker`; `InterruptedJournalNeverRestoresFromAnyActiveLifecyclePhase(Restoring)` |
| 10 | Partial start | `PartialStartRetainsUnresolvedMarkerThenConverges` |
| 11 | Start accepted, before Marker Tag removal | `RestorationConfirmsStartReadbackBeforeRemovingMarker`; `ResumeMarkedSettlesAnAcceptedStartWhoseFirstReadbackIsStillStopped` |
| 12 | Partial Marker Tag removal | `PartialMarkerRemovalRetainsIntentThenConverges` |
| 13 | Actions settled, before journal cleanup | `RestorationSettlesOnlyAfterOwnedAndPerHashIntentsConfirm`; `InterruptedJournalNeverRestoresFromAnyActiveLifecyclePhase(Restoring)` |

The cross-cutting authority invariant also has action-level proof in
`InterruptedJournalAuthorityCannotRestoreAnything`. Explicit recovery is
covered by `ResumeMarkedWithoutJournalPersistsIntentAndHonorsNeverTouch`,
`ResumeMarkedSettlesAnAcceptedStartWhoseFirstReadbackIsStillStopped`, and
`RestorePreviousSpeedPersistsIntentAndRetriesAfterFailure`.

## Real qBittorrent outage proof

The same container contract stops the real qBittorrent `5.2.3` service at two
points while Jellyfin remains running:

1. During acquisition, Jellyfin records playback and creates the activation
   journal while qBittorrent is unavailable. Status reports playback present,
   protection active, and connectivity failed. Restarting qBittorrent lets the
   same process acquire every eligible hash and enable Alternative Limits.
2. During restoration, qBittorrent is stopped after release grace begins and
   remains unavailable past the deadline. The journal remains in restoring
   state. Restarting qBittorrent lets the same process finish restoration and
   delete the journal.

Both scenarios preserve original categories, never use a public tracker, and
run only against Compose resources labeled `io.qcontrol.fixture=true`.

## Commands

```bash
scripts/dotnet.sh test Jellyfin.Plugin.QControl.sln --configuration Release
tests/fixtures/jellyfin-interruption-contract.test.sh
```
