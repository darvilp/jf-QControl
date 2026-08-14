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
attempt to infer their origin. A configurable Never-touch Tag wins over every
QControl torrent mutation.

The API's literal `stop all` and `start all` operations are rejected because a
new torrent can race between selection and tagging, and `start all` would resume
torrents that QControl never selected. During playback, periodic reconciliation
tags and stops newly eligible torrents and stops eligible marked torrents again
if they restart. QControl deliberately does not infer or preserve manual starts.

## Consequences

- Category is selection metadata, never a temporary ownership category.
- The Marker Tag is visible, configurable, durable administrator intent.
- The Never-touch Tag is the explicit exclusion and manual escape mechanism.
- Already-stopped unmarked torrents are not acquired or later started.
- “All torrents” includes categorized and uncategorized torrents but still uses
  explicit hash batches.
- Completed status is based on remaining content, not transient queued,
  stalled, or transferring states.
- The two actions may be enabled separately or together.
