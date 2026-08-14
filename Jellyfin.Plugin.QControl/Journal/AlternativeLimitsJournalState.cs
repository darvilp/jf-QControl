namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Alternative Limits observation, ownership, and mutation progress.
/// </summary>
/// <param name="InitialEnabled">The observed initial mode, or null before observation.</param>
/// <param name="EnabledByActivation">Whether this activation owns enabling the mode.</param>
/// <param name="EnableStage">Enable-operation progress.</param>
/// <param name="DisableStage">Disable-operation progress.</param>
/// <param name="ManualRestoreTarget">Explicit administrator-selected prior state.</param>
/// <param name="ManualRestoreStage">Explicit recovery mutation progress.</param>
public sealed record AlternativeLimitsJournalState(
    bool? InitialEnabled,
    bool EnabledByActivation,
    JournalMutationStage EnableStage,
    JournalMutationStage DisableStage,
    bool? ManualRestoreTarget = null,
    JournalMutationStage ManualRestoreStage = JournalMutationStage.None);
