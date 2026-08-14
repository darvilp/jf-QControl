using System;
using MediaBrowser.Controller.Session;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests;

public sealed class JellyfinSessionContractTests
{
    [Fact]
    public void SessionManagerExposesWakeEventsAndAuthoritativeSnapshot()
    {
        var contract = typeof(ISessionManager);

        Assert.NotNull(contract.GetEvent("PlaybackStart"));
        Assert.NotNull(contract.GetEvent("PlaybackProgress"));
        Assert.NotNull(contract.GetEvent("PlaybackStopped"));
        Assert.NotNull(contract.GetEvent("SessionStarted"));
        Assert.NotNull(contract.GetEvent("SessionEnded"));

        var sessions = contract.GetProperty("Sessions");
        Assert.NotNull(sessions);
        Assert.True(
            typeof(System.Collections.Generic.IReadOnlyList<SessionInfo>)
                .IsAssignableFrom(sessions.PropertyType)
            || typeof(System.Collections.Generic.IEnumerable<SessionInfo>)
                .IsAssignableFrom(sessions.PropertyType),
            $"Unexpected Sessions contract: {sessions.PropertyType}");
    }
}
