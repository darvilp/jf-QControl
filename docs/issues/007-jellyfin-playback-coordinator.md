# Issue 007 — Jellyfin playback coordinator

## Behavior

Connect the protection application service to Jellyfin's current sessions,
playback/session events, periodic reconciliation, release grace, cancellation,
and hosted-service lifecycle without blocking event threads.

## Examples

- Start event wakes reconciliation; the session snapshot, not the event payload,
  activates protection.
- A paused player continues protection.
- Two overlapping players produce one activation and release only after both
  disappear for the full grace.
- A new episode appearing during grace cancels release immediately.
- A missed start event is repaired by the inactive periodic session reread.
- A missing stop event is repaired by the periodic authoritative snapshot.
- qBittorrent failure never becomes “no playback.”

## ADRs

- ADR-0001
- ADR-0002
- ADR-0004

## TDD sequence

1. Add failing adapter tests that convert Jellyfin sessions into neutral session
   snapshots without leaking user or media data.
2. Add event-handler tests proving enqueue-and-return behavior.
3. Add a fake-clock hosted-worker test for serialization, grace, periodic work,
   retry, and cancellation.
4. Add startup and clean-shutdown tests.
5. Run the real event/session cases recorded by the compatibility spike.

## Acceptance tests

- Every playback, aggregation, timing, retry, and coordinator row in
  `TESTING.md` passes.
- At most one reconciliation mutates qBittorrent at a time.
- Event handlers perform no network or filesystem I/O.
- Paused playback has no timeout special case.
- Clean shutdown is bounded and does not claim recovery succeeded unless
  qBittorrent confirmations were received.
- Real Jellyfin evidence covers playing, paused, overlapping, and stopped
  sessions.

## Out of scope

- Media-kind, user, device, remote/local, or transcode filters.
- External Jellyfin API or webhooks.
- Automatic restoration after hard process termination.
