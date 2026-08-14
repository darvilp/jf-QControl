namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Bounded configuration-save outcome.
/// </summary>
public enum ConfigurationSaveOutcome
{
    /// <summary>The candidate was persisted and activated.</summary>
    Accepted,

    /// <summary>The candidate violated server policy.</summary>
    Invalid,

    /// <summary>The edited revision is stale.</summary>
    RevisionConflict,

    /// <summary>A required read-only connection probe failed.</summary>
    ConnectionFailed,

    /// <summary>Connection topology cannot change during an activation.</summary>
    ActiveConnectionConflict,
}
