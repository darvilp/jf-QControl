# QControl — TDD Implementation Plan

## 1. Process intent

Use one behaviorally focused issue at a time. Production behavior begins with a
failing test, and each issue finishes with targeted, full-suite, and appropriate
real-boundary evidence before the next issue starts.

The design documents are the specification. If an implementation issue exposes
an architectural contradiction, return to the design and ADRs rather than
silently choosing behavior in code.

## 2. Documentation gate

Before production code:

1. Review `CONTEXT.md`, `DESIGN.md`, and ADR-0001 through ADR-0005.
2. Walk through playing, paused, overlapping-session, queue-promotion, combined
   action, qBittorrent outage, and interruption examples.
3. Confirm the default timing and tag names.
4. Confirm the V1 compatibility boundary.
5. Resolve contradictions and keep every ADR accepted.

## 3. Ordered issues

| Order | Issue | Outcome |
|---:|---|---|
| 1 | [Repository skeleton and compatibility spike](issues/001-repository-skeleton-and-compatibility-spike.md) | Loadable empty plugin, isolated Docker environment, verified API assumptions |
| 2 | [Pure activation and policy model](issues/002-pure-activation-and-policy-model.md) | Fake-clock state machine and deterministic policy planner |
| 3 | [qBittorrent API client and credentials](issues/003-qbittorrent-api-client-and-credentials.md) | Narrow authenticated client with version negotiation and secret containment |
| 4 | [Durable activation journal](issues/004-durable-activation-journal.md) | Atomic write-ahead state and interruption detection |
| 5 | [Stop Torrents vertical slice](issues/005-stop-torrents-vertical-slice.md) | Tag-before-stop enforcement and tag-authoritative normal restoration |
| 6 | [Alternative Limits action](issues/006-alternative-limits-action.md) | Independent deterministic mode ownership and restoration |
| 7 | [Jellyfin playback coordinator](issues/007-jellyfin-playback-coordinator.md) | Direct event wake-ups, session truth, grace, polling, and serialization |
| 8 | [Configuration, status, and recovery API](issues/008-configuration-status-and-recovery-api.md) | Administrator contract, snapshots, diagnostics, and manual interruption recovery |
| 9 | [Administrator dashboard](issues/009-administrator-dashboard.md) | Thin accessible UI over validated server behavior |
| 10 | [Container proof and release hardening](issues/010-container-proof-and-release-hardening.md) | End-to-end evidence, packaging, manifest, and public-release readiness |

The order is deliberate. Issue 1 proves external contracts before adapters grow;
Issue 2 fixes semantics without frameworks; Issues 3 and 4 establish the two
critical boundaries; Issues 5 and 6 add actions independently; Issue 7 connects
them to Jellyfin; UI follows stable server behavior.

## 4. Walking slice

The first behavioral walking slice ends at Issue 5:

```text
fake playback presence
        ↓
pure activation + torrent selection
        ↓
serialized application service
        ↓
write-ahead journal
        ↓
real/stub qBittorrent tag and stop
        ↓
grace expiry and marked restore
```

This validates the deepest safety path before adding Alternative Limits or the
Jellyfin UI surface.

## 5. Issue format

Every issue specifies:

```markdown
## Behavior
User-visible or architectural behavior.

## Examples
Concrete positive, negative, and failure scenarios.

## ADRs
Relevant accepted decisions.

## TDD sequence
The first failing test and intended progression.

## Acceptance tests
Observable evidence required for completion.

## Out of scope
Related behavior deliberately deferred.
```

Issues are behavior slices, not proposed class lists. Source layout in
`DESIGN.md` is guidance and may change without rewriting tickets.

## 6. Commit and review standard

For each issue:

1. Add the smallest meaningful failing test.
2. Run it and confirm the failure expresses missing behavior.
3. Implement the minimum behavior.
4. Run targeted tests.
5. Refactor while green.
6. Run every required repository check.
7. Review against both the issue and accepted ADRs.
8. Produce one focused commit with acceptance evidence.

Do not begin the next issue, publish a package, push, or create a release unless
that action is explicitly part of the current request.
