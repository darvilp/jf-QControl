using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Privacy-safe result of validating and conditionally saving a candidate.
/// </summary>
/// <param name="Outcome">The bounded outcome.</param>
/// <param name="Configuration">The current safe configuration view.</param>
/// <param name="Failure">The bounded connection failure, if any.</param>
public sealed record ConfigurationSaveResult(
    ConfigurationSaveOutcome Outcome,
    ConfigurationView Configuration,
    JournalFailureCode? Failure);
