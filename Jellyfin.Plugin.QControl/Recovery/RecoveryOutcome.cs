namespace Jellyfin.Plugin.QControl.Recovery;

/// <summary>
/// Bounded explicit recovery outcome.
/// </summary>
public enum RecoveryOutcome
{
    /// <summary>The requested recovery reached its fixed point.</summary>
    Completed,

    /// <summary>The requested prior state or connection is unavailable.</summary>
    NotAvailable,

    /// <summary>The operation retained its durable progress after a bounded failure.</summary>
    Failed,
}
