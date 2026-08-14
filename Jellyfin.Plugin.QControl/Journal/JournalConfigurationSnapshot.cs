using System;
using System.Collections.Immutable;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Complete behavior configuration fixed for one activation.
/// </summary>
/// <param name="Revision">The accepted configuration revision.</param>
/// <param name="AlternativeLimitsEnabled">Whether Alternative Limits participates.</param>
/// <param name="StopTorrentsEnabled">Whether torrent stopping participates.</param>
/// <param name="StopScope">The snapshotted stop scope.</param>
/// <param name="SelectedCategories">Exact selected category names.</param>
/// <param name="IncludeIncomplete">Whether incomplete torrents qualify.</param>
/// <param name="IncludeCompleted">Whether completed torrents qualify.</param>
/// <param name="MarkerTag">The authoritative marker tag.</param>
/// <param name="NeverTouchTag">The dominant exclusion tag.</param>
/// <param name="ReleaseGrace">The full-absence release grace.</param>
public sealed record JournalConfigurationSnapshot(
    long Revision,
    bool AlternativeLimitsEnabled,
    bool StopTorrentsEnabled,
    TorrentScope StopScope,
    ImmutableArray<string> SelectedCategories,
    bool IncludeIncomplete,
    bool IncludeCompleted,
    string MarkerTag,
    string NeverTouchTag,
    TimeSpan ReleaseGrace);
