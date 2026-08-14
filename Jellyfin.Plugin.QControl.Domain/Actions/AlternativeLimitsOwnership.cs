namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// Tracks whether this activation owns the alternative-limits transition.
/// </summary>
/// <param name="InitialStateObserved">Whether the activation observed its initial state.</param>
/// <param name="EnabledByActivation">Whether the activation changed disabled limits to enabled.</param>
public sealed record AlternativeLimitsOwnership(
    bool InitialStateObserved,
    bool EnabledByActivation)
{
    /// <summary>
    /// Gets an ownership state before the first successful observation.
    /// </summary>
    public static AlternativeLimitsOwnership Unobserved { get; } = new(false, false);
}
