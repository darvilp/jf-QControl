using System;
using System.Collections.Generic;
using Jellyfin.Plugin.QControl.Domain.Actions;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class ActionPlannerTests
{
    [Fact]
    public void ProtectionAddsMissingMarkerBeforeStoppingSameExplicitHashes()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("unmarked", isStopped: false),
            Torrent("pre-marked", isStopped: false, tags: ["jfStopped"]),
            Torrent("excluded", isStopped: false, tags: ["jfNeverTouch"]),
            Torrent("already-stopped", isStopped: true),
        ];

        var plan = TorrentActionPlanner.PlanProtection(torrents, Policy());

        Assert.Equal(["unmarked"], plan.AddMarkerTagHashes);
        Assert.Equal(["pre-marked", "unmarked"], plan.StopHashes);
        Assert.Empty(plan.StartHashes);
        Assert.Empty(plan.RemoveMarkerTagHashes);
    }

    [Fact]
    public void RestorationStartsStoppedMarkedTorrentBeforeRemovingMarker()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("marked-stopped", isStopped: true, tags: ["jfStopped"]),
            Torrent("unmarked-stopped", isStopped: true),
            Torrent("excluded", isStopped: true, tags: ["jfStopped", "jfNeverTouch"]),
        ];

        var plan = TorrentActionPlanner.PlanRestoration(torrents, Policy());

        Assert.Equal(["marked-stopped"], plan.StartHashes);
        Assert.Empty(plan.RemoveMarkerTagHashes);
        Assert.Empty(plan.AddMarkerTagHashes);
        Assert.Empty(plan.StopHashes);
    }

    [Fact]
    public void RestorationRemovesMarkerOnlyAfterReadbackIsNotStopped()
    {
        TorrentSnapshot[] torrents =
        [
            Torrent("running", isStopped: false, tags: ["jfStopped"]),
            Torrent("queued-after-start", isStopped: false, tags: ["jfStopped"]),
            Torrent("excluded", isStopped: false, tags: ["jfStopped", "jfNeverTouch"]),
        ];

        var plan = TorrentActionPlanner.PlanRestoration(torrents, Policy());

        Assert.Empty(plan.StartHashes);
        Assert.Equal(["queued-after-start", "running"], plan.RemoveMarkerTagHashes);
    }

    [Fact]
    public void ReplanningSettledTorrentStateEmitsNoMutation()
    {
        var protection = TorrentActionPlanner.PlanProtection(
            [Torrent("protected", isStopped: true, tags: ["jfStopped"])],
            Policy());
        var restoration = TorrentActionPlanner.PlanRestoration(
            [Torrent("restored", isStopped: false)],
            Policy());

        Assert.True(protection.IsEmpty);
        Assert.True(restoration.IsEmpty);
    }

    [Fact]
    public void InitiallyDisabledAlternativeLimitsAreEnabledAndOwned()
    {
        var plan = AlternativeLimitsPlanner.PlanProtection(
            actionEnabled: true,
            currentlyEnabled: false,
            AlternativeLimitsOwnership.Unobserved);

        Assert.Equal(AlternativeLimitsMutation.Enable, plan.Mutation);
        Assert.True(plan.OwnershipAfterSuccess.InitialStateObserved);
        Assert.True(plan.OwnershipAfterSuccess.EnabledByActivation);
    }

    [Fact]
    public void InitiallyEnabledAlternativeLimitsRemainEnabledAndUnowned()
    {
        var plan = AlternativeLimitsPlanner.PlanProtection(
            actionEnabled: true,
            currentlyEnabled: true,
            AlternativeLimitsOwnership.Unobserved);

        Assert.Equal(AlternativeLimitsMutation.None, plan.Mutation);
        Assert.True(plan.OwnershipAfterSuccess.InitialStateObserved);
        Assert.False(plan.OwnershipAfterSuccess.EnabledByActivation);
    }

    [Fact]
    public void ManualDisableDuringProtectionIsReenabledWithoutChangingOwnership()
    {
        var unowned = new AlternativeLimitsOwnership(
            InitialStateObserved: true,
            EnabledByActivation: false);

        var plan = AlternativeLimitsPlanner.PlanProtection(
            actionEnabled: true,
            currentlyEnabled: false,
            unowned);

        Assert.Equal(AlternativeLimitsMutation.Enable, plan.Mutation);
        Assert.Equal(unowned, plan.OwnershipAfterSuccess);
    }

    [Fact]
    public void RestorationDisablesOnlyAnOwnedEnabledTransition()
    {
        var owned = new AlternativeLimitsOwnership(
            InitialStateObserved: true,
            EnabledByActivation: true);
        var unowned = new AlternativeLimitsOwnership(
            InitialStateObserved: true,
            EnabledByActivation: false);

        Assert.Equal(
            AlternativeLimitsMutation.Disable,
            AlternativeLimitsPlanner.PlanRestoration(true, currentlyEnabled: true, owned));
        Assert.Equal(
            AlternativeLimitsMutation.None,
            AlternativeLimitsPlanner.PlanRestoration(true, currentlyEnabled: true, unowned));
        Assert.Equal(
            AlternativeLimitsMutation.None,
            AlternativeLimitsPlanner.PlanRestoration(true, currentlyEnabled: false, owned));
    }

    [Fact]
    public void DisabledAlternativeLimitsActionAlwaysPlansNoMutation()
    {
        var protection = AlternativeLimitsPlanner.PlanProtection(
            actionEnabled: false,
            currentlyEnabled: false,
            AlternativeLimitsOwnership.Unobserved);
        var restoration = AlternativeLimitsPlanner.PlanRestoration(
            actionEnabled: false,
            currentlyEnabled: true,
            new AlternativeLimitsOwnership(true, true));

        Assert.Equal(AlternativeLimitsMutation.None, protection.Mutation);
        Assert.Equal(AlternativeLimitsOwnership.Unobserved, protection.OwnershipAfterSuccess);
        Assert.Equal(AlternativeLimitsMutation.None, restoration);
    }

    [Fact]
    public void RandomTorrentOrderingProducesTheSameDeterministicPlan()
    {
        var baseline = new[]
        {
            Torrent("charlie", isStopped: false),
            Torrent("alpha", isStopped: false, tags: ["jfStopped"]),
            Torrent("bravo", isStopped: false),
            Torrent("excluded", isStopped: false, tags: ["jfNeverTouch"]),
        };
        var expected = TorrentActionPlanner.PlanProtection(baseline, Policy());

        for (var iteration = 0; iteration < 50; iteration++)
        {
            var shuffled = Shuffle(baseline, (uint)(1977 + iteration));

            var actual = TorrentActionPlanner.PlanProtection(shuffled, Policy());

            Assert.Equal(expected.AddMarkerTagHashes, actual.AddMarkerTagHashes);
            Assert.Equal(expected.StopHashes, actual.StopHashes);
        }
    }

    private static TorrentSnapshot[] Shuffle(TorrentSnapshot[] source, uint seed)
    {
        var shuffled = new TorrentSnapshot[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            shuffled[index] = source[index];
        }

        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            seed = (1664525U * seed) + 1013904223U;
            var swapIndex = (int)(seed % (uint)(index + 1));
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }

    private static TorrentSelectionPolicy Policy()
    {
        return new TorrentSelectionPolicy(
            TorrentScope.All,
            [],
            includeIncomplete: true,
            includeCompleted: true,
            markerTag: "jfStopped",
            exclusionTags: ["jfNeverTouch"]);
    }

    private static TorrentSnapshot Torrent(
        string hash,
        bool isStopped,
        string[]? tags = null)
    {
        return new TorrentSnapshot(
            hash,
            category: null,
            remainingBytes: 10,
            isStopped,
            tags ?? []);
    }
}
