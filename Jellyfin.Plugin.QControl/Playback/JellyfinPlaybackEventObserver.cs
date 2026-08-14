using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.QControl.Playback;

/// <summary>
/// Converts Jellyfin playback and session events into non-blocking coordinator wake-ups.
/// </summary>
public sealed class JellyfinPlaybackEventObserver : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly IProtectionWakeSignal _wakeSignal;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPlaybackEventObserver"/> class.
    /// </summary>
    /// <param name="sessionManager">Jellyfin's event and snapshot source.</param>
    /// <param name="wakeSignal">The coalescing worker wake signal.</param>
    public JellyfinPlaybackEventObserver(
        ISessionManager sessionManager,
        IProtectionWakeSignal wakeSignal)
    {
        _sessionManager = sessionManager;
        _wakeSignal = wakeSignal;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionEnded += OnSessionEnded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionEnded -= OnSessionEnded;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        _wakeSignal.Wake();
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        _wakeSignal.Wake();
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs eventArgs)
    {
        _wakeSignal.Wake();
    }

    private void OnSessionStarted(object? sender, SessionEventArgs eventArgs)
    {
        _wakeSignal.Wake();
    }

    private void OnSessionEnded(object? sender, SessionEventArgs eventArgs)
    {
        _wakeSignal.Wake();
    }
}
