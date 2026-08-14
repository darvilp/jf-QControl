using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Recovery;

/// <summary>
/// Privacy-safe result of one explicit administrator recovery command.
/// </summary>
/// <param name="Outcome">The bounded command outcome.</param>
/// <param name="Failure">The bounded failure, if any.</param>
public sealed record RecoveryResult(
    RecoveryOutcome Outcome,
    JournalFailureCode? Failure = null);
