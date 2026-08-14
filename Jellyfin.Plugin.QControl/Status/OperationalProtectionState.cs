namespace Jellyfin.Plugin.QControl.Status;

/// <summary>
/// Administrator-facing protection lifecycle including conservative recovery.
/// </summary>
public enum OperationalProtectionState
{
    /// <summary>No activation exists.</summary>
    Inactive,

    /// <summary>Protection is being enforced.</summary>
    Protecting,

    /// <summary>Playback is absent and grace is running.</summary>
    ReleasePending,

    /// <summary>Owned state is being restored.</summary>
    Restoring,

    /// <summary>An interrupted or invalid journal requires explicit resolution.</summary>
    RecoveryRequired,
}
