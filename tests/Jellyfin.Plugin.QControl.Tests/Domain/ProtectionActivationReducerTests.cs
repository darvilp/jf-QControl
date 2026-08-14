using System;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class ProtectionActivationReducerTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActivationId =
        new("e6d8805d-2702-4e21-a947-29ffb17cf2db");

    [Fact]
    public void PlaybackPresenceStartsProtectionImmediately()
    {
        var request = new ActivationRequest(ActivationId, ConfigurationRevision: 7, TimeSpan.FromSeconds(60));

        var state = ProtectionActivationReducer.Reduce(
            ProtectionActivationState.Inactive,
            Present("session-a"),
            StartTime,
            request);

        Assert.Equal(ProtectionPhase.Protecting, state.Phase);
        Assert.Equal(ActivationId, state.ActivationId);
        Assert.Equal(StartTime, state.StartedAt);
        Assert.Equal(7, state.ConfigurationRevision);
        Assert.Equal(TimeSpan.FromSeconds(60), state.ReleaseGrace);
        Assert.Equal(["session-a"], state.SessionIds);
        Assert.Null(state.ReleaseDueAt);
    }

    [Fact]
    public void LastPlayerLeavingBeginsReleaseGrace()
    {
        var protecting = StartProtection();
        var absenceTime = StartTime.AddMinutes(2);

        var state = ProtectionActivationReducer.Reduce(
            protecting,
            Absent(),
            absenceTime,
            NextRequest());

        Assert.Equal(ProtectionPhase.ReleasePending, state.Phase);
        Assert.Equal(absenceTime.AddSeconds(60), state.ReleaseDueAt);
        Assert.Equal(ActivationId, state.ActivationId);
    }

    [Fact]
    public void OneOfTwoPlayersLeavingKeepsTheSameActivationProtecting()
    {
        var initial = ProtectionActivationReducer.Reduce(
            ProtectionActivationState.Inactive,
            Present("session-b", "session-a"),
            StartTime,
            new ActivationRequest(ActivationId, ConfigurationRevision: 7, TimeSpan.FromSeconds(60)));

        var state = ProtectionActivationReducer.Reduce(
            initial,
            Present("session-b"),
            StartTime.AddSeconds(30),
            NextRequest());

        Assert.Equal(ProtectionPhase.Protecting, state.Phase);
        Assert.Equal(ActivationId, state.ActivationId);
        Assert.Equal(["session-a", "session-b"], state.SessionIds);
        Assert.Null(state.ReleaseDueAt);
    }

    [Fact]
    public void PresenceBeforeGraceExpiryCancelsReleaseWithoutCreatingAnActivation()
    {
        var pending = BeginRelease();

        var state = ProtectionActivationReducer.Reduce(
            pending,
            Present("session-b"),
            pending.ReleaseDueAt!.Value.AddMilliseconds(-1),
            NextRequest());

        Assert.Equal(ProtectionPhase.Protecting, state.Phase);
        Assert.Equal(ActivationId, state.ActivationId);
        Assert.Equal(7, state.ConfigurationRevision);
        Assert.Equal(["session-a", "session-b"], state.SessionIds);
        Assert.Null(state.ReleaseDueAt);
    }

    [Fact]
    public void ExactGraceBoundaryBeginsRestorationWhenPresenceIsStillAbsent()
    {
        var pending = BeginRelease();

        var state = ProtectionActivationReducer.Reduce(
            pending,
            Absent(),
            pending.ReleaseDueAt!.Value,
            NextRequest());

        Assert.Equal(ProtectionPhase.Restoring, state.Phase);
        Assert.Equal(ActivationId, state.ActivationId);
    }

    [Fact]
    public void PresenceAtExactBoundaryStillCancelsRelease()
    {
        var pending = BeginRelease();

        var state = ProtectionActivationReducer.Reduce(
            pending,
            Present("session-b"),
            pending.ReleaseDueAt!.Value,
            NextRequest());

        Assert.Equal(ProtectionPhase.Protecting, state.Phase);
        Assert.Equal(ActivationId, state.ActivationId);
    }

    [Fact]
    public void ActiveConfigurationAndGraceRemainSnapshotted()
    {
        var pending = BeginRelease();
        var changedRequest = new ActivationRequest(
            Guid.NewGuid(),
            ConfigurationRevision: 8,
            TimeSpan.FromSeconds(5));

        var state = ProtectionActivationReducer.Reduce(
            pending,
            Absent(),
            pending.ReleaseDueAt!.Value.AddSeconds(-30),
            changedRequest);

        Assert.Equal(ActivationId, state.ActivationId);
        Assert.Equal(7, state.ConfigurationRevision);
        Assert.Equal(TimeSpan.FromSeconds(60), state.ReleaseGrace);
        Assert.Equal(StartTime.AddMinutes(3), state.ReleaseDueAt);
    }

    [Fact]
    public void SettledRestorationReturnsToInactive()
    {
        var restoring = ProtectionActivationReducer.Reduce(
            BeginRelease(),
            Absent(),
            StartTime.AddMinutes(3),
            NextRequest());

        var state = ProtectionActivationReducer.CompleteRestoration(restoring);

        Assert.Same(ProtectionActivationState.Inactive, state);
    }

    [Fact]
    public void RepeatedAbsenceAfterGraceExpiryDoesNotRetriggerRestoration()
    {
        var restoring = ProtectionActivationReducer.Reduce(
            BeginRelease(),
            Absent(),
            StartTime.AddMinutes(3),
            NextRequest());

        var state = ProtectionActivationReducer.Reduce(
            restoring,
            Absent(),
            StartTime.AddMinutes(4),
            NextRequest());

        Assert.Same(restoring, state);
    }

    private static ProtectionActivationState StartProtection()
    {
        return ProtectionActivationReducer.Reduce(
            ProtectionActivationState.Inactive,
            Present("session-a"),
            StartTime,
            new ActivationRequest(ActivationId, ConfigurationRevision: 7, TimeSpan.FromSeconds(60)));
    }

    private static ProtectionActivationState BeginRelease()
    {
        return ProtectionActivationReducer.Reduce(
            StartProtection(),
            Absent(),
            StartTime.AddMinutes(2),
            NextRequest());
    }

    private static ActivationRequest NextRequest()
    {
        return new ActivationRequest(Guid.NewGuid(), ConfigurationRevision: 99, TimeSpan.FromSeconds(5));
    }

    private static PlaybackPresenceSnapshot Present(params string[] sessionIds)
    {
        return new PlaybackPresenceSnapshot(isPresent: true, sessionIds);
    }

    private static PlaybackPresenceSnapshot Absent()
    {
        return new PlaybackPresenceSnapshot(isPresent: false, []);
    }
}
