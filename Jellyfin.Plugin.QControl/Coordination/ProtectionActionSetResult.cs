using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// The privacy-safe outcome of one complete action pass.
/// </summary>
/// <param name="Journal">The latest document durably written by action services.</param>
/// <param name="RestorationSettled">Whether every enabled restoration action is settled.</param>
/// <param name="Failure">The bounded failure category, if any action failed.</param>
public sealed record ProtectionActionSetResult(
    ActivationJournalDocument Journal,
    bool RestorationSettled,
    JournalFailureCode? Failure);
