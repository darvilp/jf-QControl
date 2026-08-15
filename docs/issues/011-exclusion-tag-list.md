# Issue 011 — Creatable Exclusion Tag List

## Behavior

Replace the single Never-touch Tag with a snapshotted Exclusion Tag List. Any
exact, case-sensitive match prevents every QControl torrent mutation. The
administrator dashboard offers the complete registered qBittorrent tag catalog
as optional suggestions while accepting new values that do not yet exist.

Fresh alpha installations default to Marker Tag `qcontrol-resume` and one
Exclusion Tag, `qcontrol-ignore`. Alpha configuration and journal formats may
start clean; this issue does not add legacy-field migration.

## Examples

- A torrent tagged `manual` is excluded when the configured list contains
  `manual`, even if another configured exclusion does not match.
- `Manual` does not match `manual`; internal spaces and Unicode remain exact.
- An empty list is valid after the administrator deliberately removes the
  default.
- Surrounding whitespace and exact duplicates normalize away; blank, comma,
  line-break, control-character, overlength, and over-count input is rejected.
- A custom configured tag remains visible when qBittorrent is offline or does
  not register it.
- Failure to read the global tag catalog leaves connection validation usable
  and reports only that suggestions are unavailable.
- Adding a snapshotted exclusion to an already stopped and marked torrent makes
  QControl leave it untouched until the actual exclusion is removed.

## ADRs

- ADR-0002
- ADR-0003
- ADR-0004

## TDD sequence

1. Extend the pure torrent-selection seam with any-match, empty-list,
   normalization, conflict, and boundary examples.
2. Carry the immutable list through fresh configuration, activation snapshots,
   journals, status, and manual recovery.
3. Add the allowlisted read-only global-tag catalog operation and make catalog
   failure non-blocking for connection validation.
4. Replace the dashboard scalar input with a staged, accessible creatable list
   while retaining the Marker Tag as plain text.
5. Prove the packaged flow against the isolated qBittorrent and Jellyfin
   fixtures, including multiple exclusions and unavailable suggestions.

## Acceptance tests

- Any configured Exclusion Tag dominates the Marker Tag at acquisition,
  enforcement, normal restoration, and manual recovery.
- The list accepts zero through 64 unique normalized entries, each no longer
  than 128 characters.
- Saving configuration never creates or assigns an Exclusion Tag in
  qBittorrent.
- Configuration changes remain pending until the next activation; actual tag
  assignments are observed on every reconciliation.
- Global catalog suggestions merge with configured custom values without
  forcing either to exist in the other set.
- The administrator can add and remove entries with keyboard-native controls,
  then persist them only through Save configuration.
- Configuration, status, journal, logs, and browser output contain no credential
  or private torrent/media display data.
- Focused tests, the full suite, package verification, qBittorrent contracts,
  and the administrator browser fixture pass.

## Out of scope

- Creating, deleting, or assigning exclusion tags in qBittorrent.
- Multiple Marker Tags.
- Live replacement of an active configuration snapshot.
- Migration from alpha configuration or journal schemas.
