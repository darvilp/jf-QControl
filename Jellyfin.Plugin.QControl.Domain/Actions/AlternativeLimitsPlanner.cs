using System;

namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// Plans ownership-safe global alternative-limits reconciliation.
/// </summary>
public static class AlternativeLimitsPlanner
{
    /// <summary>
    /// Plans protection reconciliation and the ownership state to commit on success.
    /// </summary>
    /// <param name="actionEnabled">Whether the action is enabled by configuration.</param>
    /// <param name="currentlyEnabled">Whether qBittorrent currently reports alternative limits enabled.</param>
    /// <param name="ownership">The current activation ownership state.</param>
    /// <returns>The requested mutation and resulting ownership.</returns>
    public static AlternativeLimitsPlan PlanProtection(
        bool actionEnabled,
        bool currentlyEnabled,
        AlternativeLimitsOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);

        if (!actionEnabled)
        {
            return new AlternativeLimitsPlan(AlternativeLimitsMutation.None, ownership);
        }

        if (!ownership.InitialStateObserved)
        {
            return currentlyEnabled
                ? new AlternativeLimitsPlan(
                    AlternativeLimitsMutation.None,
                    new AlternativeLimitsOwnership(true, false))
                : new AlternativeLimitsPlan(
                    AlternativeLimitsMutation.Enable,
                    new AlternativeLimitsOwnership(true, true));
        }

        var mutation = currentlyEnabled
            ? AlternativeLimitsMutation.None
            : AlternativeLimitsMutation.Enable;
        return new AlternativeLimitsPlan(mutation, ownership);
    }

    /// <summary>
    /// Plans restoration without disabling an administrator-owned enabled state.
    /// </summary>
    /// <param name="actionEnabled">Whether the action is enabled by configuration.</param>
    /// <param name="currentlyEnabled">Whether qBittorrent currently reports alternative limits enabled.</param>
    /// <param name="ownership">The activation ownership state.</param>
    /// <returns>The requested mutation.</returns>
    public static AlternativeLimitsMutation PlanRestoration(
        bool actionEnabled,
        bool currentlyEnabled,
        AlternativeLimitsOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);

        return actionEnabled && ownership.EnabledByActivation && currentlyEnabled
            ? AlternativeLimitsMutation.Disable
            : AlternativeLimitsMutation.None;
    }
}
