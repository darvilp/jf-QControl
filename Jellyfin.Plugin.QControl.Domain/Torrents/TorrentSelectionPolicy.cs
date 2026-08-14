using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Torrents;

/// <summary>
/// Immutable stop-action selection policy for one activation.
/// </summary>
public sealed record TorrentSelectionPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TorrentSelectionPolicy"/> class.
    /// </summary>
    /// <param name="scope">The category scope.</param>
    /// <param name="selectedCategories">The exact selected category names.</param>
    /// <param name="includeIncomplete">Whether incomplete torrents qualify.</param>
    /// <param name="includeCompleted">Whether completed torrents qualify.</param>
    /// <param name="markerTag">The activation marker tag.</param>
    /// <param name="neverTouchTag">The dominant exclusion tag.</param>
    public TorrentSelectionPolicy(
        TorrentScope scope,
        IEnumerable<string> selectedCategories,
        bool includeIncomplete,
        bool includeCompleted,
        string markerTag,
        string neverTouchTag)
    {
        ArgumentNullException.ThrowIfNull(selectedCategories);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown torrent scope.");
        }

        if (!includeIncomplete && !includeCompleted)
        {
            throw new ArgumentException("At least one torrent lifecycle must be included.");
        }

        if (string.IsNullOrWhiteSpace(markerTag))
        {
            throw new ArgumentException("Marker tag cannot be empty.", nameof(markerTag));
        }

        if (string.IsNullOrWhiteSpace(neverTouchTag))
        {
            throw new ArgumentException("Never-touch tag cannot be empty.", nameof(neverTouchTag));
        }

        if (string.Equals(markerTag, neverTouchTag, StringComparison.Ordinal))
        {
            throw new ArgumentException("Marker and never-touch tags must be distinct.");
        }

        var categories = selectedCategories.ToFrozenSet(StringComparer.Ordinal);
        if (categories.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Selected category names cannot be empty.", nameof(selectedCategories));
        }

        if (scope == TorrentScope.SelectedCategories && categories.Count == 0)
        {
            throw new ArgumentException(
                "Selected-category scope requires at least one category.",
                nameof(selectedCategories));
        }

        Scope = scope;
        SelectedCategories = categories;
        IncludeIncomplete = includeIncomplete;
        IncludeCompleted = includeCompleted;
        MarkerTag = markerTag;
        NeverTouchTag = neverTouchTag;
    }

    /// <summary>
    /// Gets the category scope.
    /// </summary>
    public TorrentScope Scope { get; }

    /// <summary>
    /// Gets the exact selected category names.
    /// </summary>
    public IReadOnlySet<string> SelectedCategories { get; }

    /// <summary>
    /// Gets a value indicating whether incomplete torrents qualify.
    /// </summary>
    public bool IncludeIncomplete { get; }

    /// <summary>
    /// Gets a value indicating whether completed torrents qualify.
    /// </summary>
    public bool IncludeCompleted { get; }

    /// <summary>
    /// Gets the marker tag.
    /// </summary>
    public string MarkerTag { get; }

    /// <summary>
    /// Gets the dominant exclusion tag.
    /// </summary>
    public string NeverTouchTag { get; }
}
