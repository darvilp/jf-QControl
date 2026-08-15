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
    /// <summary>The maximum number of configured exclusion tags.</summary>
    public const int MaximumExclusionTagCount = 64;

    /// <summary>The maximum normalized length of one exclusion tag.</summary>
    public const int MaximumTagLength = 128;

    /// <summary>
    /// Initializes a new instance of the <see cref="TorrentSelectionPolicy"/> class.
    /// </summary>
    /// <param name="scope">The category scope.</param>
    /// <param name="selectedCategories">The exact selected category names.</param>
    /// <param name="includeIncomplete">Whether incomplete torrents qualify.</param>
    /// <param name="includeCompleted">Whether completed torrents qualify.</param>
    /// <param name="markerTag">The activation marker tag.</param>
    /// <param name="exclusionTags">The dominant exclusion tags.</param>
    public TorrentSelectionPolicy(
        TorrentScope scope,
        IEnumerable<string> selectedCategories,
        bool includeIncomplete,
        bool includeCompleted,
        string markerTag,
        IEnumerable<string> exclusionTags)
    {
        ArgumentNullException.ThrowIfNull(selectedCategories);
        ArgumentNullException.ThrowIfNull(exclusionTags);
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

        var exclusions = NormalizeExclusionTags(exclusionTags)
            .ToFrozenSet(StringComparer.Ordinal);

        if (exclusions.Contains(markerTag))
        {
            throw new ArgumentException("Marker tag cannot also be an exclusion tag.");
        }

        Scope = scope;
        SelectedCategories = categories;
        IncludeIncomplete = includeIncomplete;
        IncludeCompleted = includeCompleted;
        MarkerTag = markerTag;
        ExclusionTags = exclusions;
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
    /// Gets the dominant exclusion tags.
    /// </summary>
    public IReadOnlySet<string> ExclusionTags { get; }

    /// <summary>
    /// Normalizes and validates one administrator-supplied exclusion list.
    /// </summary>
    /// <param name="exclusionTags">The supplied exact tags.</param>
    /// <returns>Unique normalized tags in deterministic ordinal order.</returns>
    public static IReadOnlyList<string> NormalizeExclusionTags(IEnumerable<string> exclusionTags)
    {
        ArgumentNullException.ThrowIfNull(exclusionTags);
        var normalized = exclusionTags
            .Select(tag => (tag ?? string.Empty).Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Exclusion tags cannot be empty.", nameof(exclusionTags));
        }

        if (normalized.Length > MaximumExclusionTagCount)
        {
            throw new ArgumentException(
                $"At most {MaximumExclusionTagCount} exclusion tags are allowed.",
                nameof(exclusionTags));
        }

        if (normalized.Any(tag => tag.Length > MaximumTagLength
                || tag.Contains(',', StringComparison.Ordinal)
                || tag.Any(char.IsControl)))
        {
            throw new ArgumentException(
                "Exclusion tags cannot exceed the length limit or contain delimiters or controls.",
                nameof(exclusionTags));
        }

        return Array.AsReadOnly(normalized);
    }

    /// <summary>
    /// Determines whether any configured exclusion tag is present.
    /// </summary>
    /// <param name="torrent">The neutral torrent snapshot.</param>
    /// <returns><see langword="true"/> when the torrent is excluded.</returns>
    public bool IsExcluded(TorrentSnapshot torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.Tags.Any(ExclusionTags.Contains);
    }
}
