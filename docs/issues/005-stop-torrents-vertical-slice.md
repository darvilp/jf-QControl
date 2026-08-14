# Issue 005 — Stop Torrents vertical slice

## Behavior

Deliver the first complete protection action through a serialized application
service: select eligible torrents, journal intent, add the Marker Tag, stop the
same explicit hashes, continually enforce protection, and normally restore
marked non-excluded torrents after release grace.

## Examples

- Five running category-matched torrents are tagged and stopped in a batch.
- Five queued replacements are included initially or caught on the next pass;
  previously stopped torrents are never deliberately cycled.
- A torrent added during playback is tagged and stopped within the periodic
  reconciliation bound.
- A manually restarted eligible torrent is stopped again.
- A pre-existing marked torrent is restarted on normal release because the tag
  is administrator intent.
- An unmarked torrent that was already stopped remains stopped.

## ADRs

- ADR-0002
- ADR-0003
- ADR-0004

## TDD sequence

1. Add a failing application test for the journal-before-tag-before-stop order.
2. Add the minimal serialized executor over fake qBittorrent and journal ports.
3. Add all/category, lifecycle, queue-promotion, new-torrent, and repeated-pass
   scenarios.
4. Add normal release tests for start-readback-before-marker-removal and
   unmarked-stop preservation.
5. Add Never-touch dominance and partial-operation failures.
6. Run the vertical slice against pinned qBittorrent fixtures.

## Acceptance tests

- All Stop Torrents acquisition, enforcement, restoration, and partial-failure
  rows in `TESTING.md` pass.
- Every stopped hash was in the successful marker batch for that operation.
- No request uses `stop all` or `start all`.
- Category never changes.
- Repeated reconciliation reaches a fixed point with no further mutations.
- A qBittorrent outage retains activation and journal state without starting
  anything.

## Out of scope

- Alternative Limits.
- Real Jellyfin event subscriptions.
- Dashboard UI.
- Manual-start detection or preservation.
