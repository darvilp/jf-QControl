using System;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class TorrentSelectorTests
{
    [Fact]
    public void AllScopeIncludesCategorizedAndUncategorizedTorrents()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("categorized", category: "sonarr", remainingBytes: 10),
            Torrent("uncategorized", category: null, remainingBytes: 10),
        ];

        var hashes = TorrentSelector.SelectForAcquisition(torrents, AllLifecycles());

        Assert.Equal(["categorized", "uncategorized"], hashes);
    }

    [Fact]
    public void SelectedCategoriesUseExactOrdinalNames()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("exact", category: "sonarr", remainingBytes: 10),
            Torrent("case-variant", category: "Sonarr", remainingBytes: 10),
            Torrent("other", category: "radarr", remainingBytes: 10),
            Torrent("none", category: null, remainingBytes: 10),
        ];
        var policy = new TorrentSelectionPolicy(
            TorrentScope.SelectedCategories,
            ["sonarr"],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);

        var hashes = TorrentSelector.SelectForAcquisition(torrents, policy);

        Assert.Equal(["exact"], hashes);
    }

    [Fact]
    public void LifecycleUsesRemainingContentRatherThanTransientState()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("completed-queued-seed", category: "radarr", remainingBytes: 0),
            Torrent("incomplete-download", category: "radarr", remainingBytes: 1),
        ];
        var completedOnly = new TorrentSelectionPolicy(
            TorrentScope.SelectedCategories,
            ["radarr"],
            includeIncomplete: false,
            includeCompleted: true,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);

        var hashes = TorrentSelector.SelectForAcquisition(torrents, completedOnly);

        Assert.Equal(["completed-queued-seed"], hashes);
    }

    [Fact]
    public void IncompleteOnlyExcludesCompletedTorrents()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("complete", category: null, remainingBytes: 0),
            Torrent("incomplete", category: null, remainingBytes: 1),
        ];
        var policy = new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: false,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);

        var hashes = TorrentSelector.SelectForAcquisition(torrents, policy);

        Assert.Equal(["incomplete"], hashes);
    }

    [Fact]
    public void StoppedTorrentIsNotAcquiredOrMarked()
    {
        var torrents = new[]
        {
            Torrent("already-stopped", category: null, remainingBytes: 10, isStopped: true),
        };

        var hashes = TorrentSelector.SelectForAcquisition(torrents, AllLifecycles());

        Assert.Empty(hashes);
    }

    [Fact]
    public void NeverTouchWinsWhenBothTagsArePresent()
    {
        var torrents = new[]
        {
            Torrent(
                "excluded",
                category: null,
                remainingBytes: 10,
                tags: ["jfStopped", "jfNeverTouch"]),
            Torrent(
                "pre-marked-running",
                category: null,
                remainingBytes: 10,
                tags: ["jfStopped"]),
        };

        var hashes = TorrentSelector.SelectForAcquisition(torrents, AllLifecycles());

        Assert.Equal(["pre-marked-running"], hashes);
    }

    [Fact]
    public void AnyConfiguredExclusionTagWinsAndAnEmptyListExcludesNothing()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("manual", category: null, remainingBytes: 10, tags: ["manual"]),
            Torrent("cross-seed", category: null, remainingBytes: 10, tags: ["cross-seed"]),
            Torrent("ordinary", category: null, remainingBytes: 10),
        ];
        var exclusions = new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "qcontrol-resume",
            exclusionTags: ["manual", "cross-seed"]);
        var noExclusions = new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "qcontrol-resume",
            exclusionTags: []);

        Assert.Equal(["ordinary"], TorrentSelector.SelectForAcquisition(torrents, exclusions));
        Assert.Equal(
            ["cross-seed", "manual", "ordinary"],
            TorrentSelector.SelectForAcquisition(torrents, noExclusions));
    }

    [Fact]
    public void ExclusionTagsNormalizeBoundariesAndExactDuplicatesOnly()
    {
        var policy = new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "qcontrol-resume",
            exclusionTags: [" manual ", "manual", "Manual", "Do Not Stop"]);

        Assert.Equal(3, policy.ExclusionTags.Count);
        Assert.Contains("manual", policy.ExclusionTags);
        Assert.Contains("Manual", policy.ExclusionTags);
        Assert.Contains("Do Not Stop", policy.ExclusionTags);
        Assert.DoesNotContain(" manual ", policy.ExclusionTags);
    }

    [Fact]
    public void InvalidOrUnboundedTagPoliciesAreRejected()
    {
        AssertInvalidExclusions(["blank", " "]);
        AssertInvalidExclusions(["comma,delimiter"]);
        AssertInvalidExclusions(["line\nbreak"]);
        AssertInvalidExclusions(["control\u0001character"]);
        AssertInvalidExclusions([new string('x', 129)]);

        var tooMany = new string[65];
        for (var index = 0; index < tooMany.Length; index++)
        {
            tooMany[index] = $"tag-{index}";
        }

        AssertInvalidExclusions(tooMany);
        AssertInvalidExclusions(["qcontrol-resume"]);
    }

    [Fact]
    public void InvalidLifecycleAndTagPoliciesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: false,
            includeCompleted: false,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]));

        Assert.Throws<ArgumentException>(() => new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "same",
            exclusionTags: ["same"]));
    }

    [Fact]
    public void LiteralAllHashIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Torrent(
            "all",
            category: null,
            remainingBytes: 10));
    }

    private static TorrentSelectionPolicy AllLifecycles()
    {
        return new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);
    }

    private static void AssertInvalidExclusions(string[] exclusionTags)
    {
        Assert.Throws<ArgumentException>(() => new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "qcontrol-resume",
            exclusionTags));
    }

    private static TorrentSnapshot Torrent(
        string hash,
        string? category,
        long remainingBytes,
        bool isStopped = false,
        string[]? tags = null)
    {
        return new TorrentSnapshot(
            hash,
            category,
            remainingBytes,
            isStopped,
            tags ?? []);
    }
}
