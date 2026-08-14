namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Invalidates coordinator cache after an explicitly serialized recovery operation.
/// </summary>
public interface IProtectionCoordinatorStateControl
{
    /// <summary>
    /// Forces the next reconciliation to reload durable journal authority.
    /// The caller must hold <see cref="IProtectionExecutionGate"/>.
    /// </summary>
    void InvalidateJournalCache();
}
