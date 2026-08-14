namespace Jellyfin.Plugin.QControl.Domain.Activation;

/// <summary>
/// The lifecycle phase of one protection activation.
/// </summary>
public enum ProtectionPhase
{
    /// <summary>
    /// No activation exists.
    /// </summary>
    Inactive,

    /// <summary>
    /// Protection is required and should be enforced.
    /// </summary>
    Protecting,

    /// <summary>
    /// Playback is absent while the release grace elapses.
    /// </summary>
    ReleasePending,

    /// <summary>
    /// Owned qBittorrent state is being restored.
    /// </summary>
    Restoring,
}
