namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// A global alternative-limits mutation and the ownership committed on success.
/// </summary>
/// <param name="Mutation">The requested mutation.</param>
/// <param name="OwnershipAfterSuccess">The ownership to persist after successful reconciliation.</param>
public sealed record AlternativeLimitsPlan(
    AlternativeLimitsMutation Mutation,
    AlternativeLimitsOwnership OwnershipAfterSuccess);
