---
status: accepted
---

# Use independent actions and tag-authoritative torrent control

QControl will expose Alternative Limits and Stop Torrents as independent
actions. Stop Torrents selects all torrents or configured categories, then
independently includes incomplete and completed lifecycles. It adds a
configurable Marker Tag before stopping explicit hashes and later starts marked
hashes before removing the tag. Existing Marker Tag assignments are presumed
intentional administrator state; QControl does not warn about collisions or
attempt to infer their origin. A configurable Exclusion Tag List supports exact,
case-sensitive tags that need not already exist in qBittorrent; a match on any
member wins over every QControl torrent mutation.

Exclusion entries are normalized only at their boundaries: surrounding
whitespace is trimmed, blank and comma-bearing values are rejected, exact
duplicates are removed, and case plus internal spaces remain significant.
Suggestions come from qBittorrent's complete registered tag catalog. Saving
configuration never creates or assigns an Exclusion Tag in qBittorrent.
The list is fixed in the activation snapshot, while actual tag assignments are
observed on every reconciliation. An excluded marked torrent is not resumed or
unmarked until its exclusion is removed and restoration is requested again.

The API's literal `stop all` and `start all` operations are rejected because a
new torrent can race between selection and tagging, and `start all` would resume
torrents that QControl never selected. During playback, periodic reconciliation
tags and stops newly eligible torrents and stops eligible marked torrents again
if they restart. QControl deliberately does not infer or preserve manual starts.

## Consequences

- Category is selection metadata, never a temporary ownership category.
- The Marker Tag is visible, configurable, durable administrator intent.
- Any Exclusion Tag is an explicit exclusion and manual escape mechanism.
- The Exclusion Tag List may be empty when the administrator deliberately
  removes its default member.
- Configuration changes do not rewrite an active protection snapshot.
- Already-stopped unmarked torrents are not acquired or later started.
- “All torrents” includes categorized and uncategorized torrents but still uses
  explicit hash batches.
- Completed status is based on remaining content, not transient queued,
  stalled, or transferring states.
- The two actions may be enabled separately or together.
