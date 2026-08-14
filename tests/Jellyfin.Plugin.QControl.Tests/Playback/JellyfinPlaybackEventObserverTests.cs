using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Playback;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Playback;

public sealed class JellyfinPlaybackEventObserverTests
{
    [Fact]
    public async Task RelevantEventsOnlySignalAndReturnSynchronously()
    {
        var sessions = new Mock<ISessionManager>();
        var wake = new RecordingWakeSignal();
        var observer = new JellyfinPlaybackEventObserver(
            sessions.Object,
            wake);
        await observer.StartAsync(CancellationToken.None);

        sessions.Raise(manager => manager.PlaybackStart += null, new PlaybackProgressEventArgs());
        sessions.Raise(manager => manager.PlaybackProgress += null, new PlaybackProgressEventArgs());
        sessions.Raise(manager => manager.PlaybackStopped += null, new PlaybackStopEventArgs());
        sessions.Raise(
            manager => manager.SessionStarted += null,
            new SessionEventArgs());
        sessions.Raise(
            manager => manager.SessionEnded += null,
            new SessionEventArgs());

        Assert.Equal(5, wake.Count);
        await observer.StopAsync(CancellationToken.None);
        sessions.Raise(manager => manager.PlaybackStart += null, new PlaybackProgressEventArgs());
        Assert.Equal(5, wake.Count);
    }

    private sealed class RecordingWakeSignal : IProtectionWakeSignal
    {
        public int Count { get; private set; }

        public void Wake()
        {
            Count++;
        }
    }
}
