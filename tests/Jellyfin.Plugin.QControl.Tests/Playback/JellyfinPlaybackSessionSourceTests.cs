using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Playback;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Playback;

public sealed class JellyfinPlaybackSessionSourceTests
{
    [Fact]
    public async Task ReadMapsOnlyNeutralPlaybackStateInStableOrder()
    {
        var sessionManager = new Mock<ISessionManager>();
        var paused = CreateSession(sessionManager.Object, "session-b", hasMedia: true, isPaused: true);
        paused.UserName = "private-user";
        paused.DeviceName = "private-device";
        var connected = CreateSession(
            sessionManager.Object,
            "session-a",
            hasMedia: false,
            isPaused: false);
        sessionManager.SetupGet(manager => manager.Sessions)
            .Returns(new List<SessionInfo> { paused, connected });
        var source = new JellyfinPlaybackSessionSource(sessionManager.Object);

        var result = await source.ReadAsync(CancellationToken.None);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("session-a", first.SessionId);
                Assert.False(first.HasCurrentMedia);
                Assert.False(first.IsPaused);
            },
            second =>
            {
                Assert.Equal("session-b", second.SessionId);
                Assert.True(second.HasCurrentMedia);
                Assert.True(second.IsPaused);
            });
        Assert.All(result, snapshot =>
        {
            Assert.DoesNotContain("private-user", snapshot.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("private-device", snapshot.ToString(), StringComparison.Ordinal);
        });
    }

    private static SessionInfo CreateSession(
        ISessionManager sessionManager,
        string id,
        bool hasMedia,
        bool isPaused)
    {
        return new SessionInfo(sessionManager, NullLogger.Instance)
        {
            Id = id,
            NowPlayingItem = hasMedia ? new BaseItemDto() : null,
            PlayState = new PlayerStateInfo { IsPaused = isPaused },
        };
    }
}
