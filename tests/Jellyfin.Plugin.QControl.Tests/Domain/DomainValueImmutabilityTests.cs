using System;
using System.Collections.Generic;
using Jellyfin.Plugin.QControl.Domain.Actions;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class DomainValueImmutabilityTests
{
    [Fact]
    public void PresenceSnapshotCopiesCallerOwnedSessionIdentifiers()
    {
        var callerOwned = new[] { "original" };
        var snapshot = new PlaybackPresenceSnapshot(isPresent: true, callerOwned);

        callerOwned[0] = "changed";

        Assert.Equal(["original"], snapshot.SessionIds);
    }

    [Fact]
    public void TorrentSelectorReturnsAnImmutableHashList()
    {
        var policy = new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);
        var selected = TorrentSelector.SelectForAcquisition(
            [new TorrentSnapshot("original", null, 1, false, [])],
            policy);

        var mutableView = Assert.IsAssignableFrom<IList<string>>(selected);

        Assert.Throws<NotSupportedException>(() => mutableView[0] = "changed");
    }

    [Fact]
    public void MutationPlanCopiesCallerOwnedHashLists()
    {
        var callerOwned = new[] { "original" };
        var plan = new TorrentMutationPlan(callerOwned, [], [], []);

        callerOwned[0] = "changed";

        Assert.Equal(["original"], plan.AddMarkerTagHashes);
    }
}
