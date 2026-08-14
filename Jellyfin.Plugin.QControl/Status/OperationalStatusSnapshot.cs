using System;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Status;

/// <summary>
/// Complete privacy-safe operational status for the administrator API.
/// </summary>
public sealed record OperationalStatusSnapshot
{
    /// <summary>Initializes a new instance of the <see cref="OperationalStatusSnapshot"/> class.</summary>
    /// <param name="connectivity">The qBittorrent connectivity state.</param>
    /// <param name="applicationVersion">The compatible application version.</param>
    /// <param name="webApiVersion">The compatible Web API version.</param>
    /// <param name="protectionState">The administrator-facing lifecycle.</param>
    /// <param name="qualifyingSessionCount">The current qualifying session count.</param>
    /// <param name="alternativeLimitsActionEnabled">Whether limits participates.</param>
    /// <param name="stopTorrentsActionEnabled">Whether torrent stopping participates.</param>
    /// <param name="alternativeLimitsCurrentlyEnabled">The current limits mode.</param>
    /// <param name="alternativeLimitsOwned">Whether the activation owns the limits transition.</param>
    /// <param name="eligibleTorrentCount">The currently eligible running torrent count.</param>
    /// <param name="markedTorrentCount">The marked torrent count.</param>
    /// <param name="stoppedMarkedTorrentCount">The marked and stopped count.</param>
    /// <param name="excludedTorrentCount">The never-touch count.</param>
    /// <param name="releaseGraceRemainingSeconds">The rounded-up release countdown.</param>
    /// <param name="configurationChangesPending">Whether saved behavior waits.</param>
    /// <param name="currentConfigurationRevision">The saved revision.</param>
    /// <param name="activationConfigurationRevision">The snapshotted revision.</param>
    /// <param name="lastSuccessfulReconciliation">The last successful pass.</param>
    /// <param name="currentError">The current bounded error.</param>
    /// <param name="canResumeMarkedTorrents">Whether marked recovery applies.</param>
    /// <param name="canRestorePreviousSpeedSetting">Whether prior limits are known.</param>
    /// <param name="canMarkResolved">Whether explicit resolution applies.</param>
    public OperationalStatusSnapshot(
        QbittorrentConnectivity connectivity,
        string? applicationVersion,
        string? webApiVersion,
        OperationalProtectionState protectionState,
        int qualifyingSessionCount,
        bool alternativeLimitsActionEnabled,
        bool stopTorrentsActionEnabled,
        bool? alternativeLimitsCurrentlyEnabled,
        bool alternativeLimitsOwned,
        int? eligibleTorrentCount,
        int? markedTorrentCount,
        int? stoppedMarkedTorrentCount,
        int? excludedTorrentCount,
        int? releaseGraceRemainingSeconds,
        bool configurationChangesPending,
        long currentConfigurationRevision,
        long? activationConfigurationRevision,
        DateTimeOffset? lastSuccessfulReconciliation,
        JournalFailureCode? currentError,
        bool canResumeMarkedTorrents,
        bool canRestorePreviousSpeedSetting,
        bool canMarkResolved)
    {
        Connectivity = connectivity;
        ApplicationVersion = applicationVersion;
        WebApiVersion = webApiVersion;
        ProtectionState = protectionState;
        QualifyingSessionCount = qualifyingSessionCount;
        AlternativeLimitsActionEnabled = alternativeLimitsActionEnabled;
        StopTorrentsActionEnabled = stopTorrentsActionEnabled;
        AlternativeLimitsCurrentlyEnabled = alternativeLimitsCurrentlyEnabled;
        AlternativeLimitsOwned = alternativeLimitsOwned;
        EligibleTorrentCount = eligibleTorrentCount;
        MarkedTorrentCount = markedTorrentCount;
        StoppedMarkedTorrentCount = stoppedMarkedTorrentCount;
        ExcludedTorrentCount = excludedTorrentCount;
        ReleaseGraceRemainingSeconds = releaseGraceRemainingSeconds;
        ConfigurationChangesPending = configurationChangesPending;
        CurrentConfigurationRevision = currentConfigurationRevision;
        ActivationConfigurationRevision = activationConfigurationRevision;
        LastSuccessfulReconciliation = lastSuccessfulReconciliation;
        CurrentError = currentError;
        CanResumeMarkedTorrents = canResumeMarkedTorrents;
        CanRestorePreviousSpeedSetting = canRestorePreviousSpeedSetting;
        CanMarkResolved = canMarkResolved;
    }

    /// <summary>Gets qBittorrent connectivity.</summary>
    public QbittorrentConnectivity Connectivity { get; }

    /// <summary>Gets the compatible qBittorrent application version.</summary>
    public string? ApplicationVersion { get; }

    /// <summary>Gets the compatible Web API version.</summary>
    public string? WebApiVersion { get; }

    /// <summary>Gets the administrator-facing lifecycle.</summary>
    public OperationalProtectionState ProtectionState { get; }

    /// <summary>Gets the current qualifying Jellyfin session count.</summary>
    public int QualifyingSessionCount { get; }

    /// <summary>Gets a value indicating whether Alternative Limits participates.</summary>
    public bool AlternativeLimitsActionEnabled { get; }

    /// <summary>Gets a value indicating whether Stop Torrents participates.</summary>
    public bool StopTorrentsActionEnabled { get; }

    /// <summary>Gets the current Alternative Limits mode when readable.</summary>
    public bool? AlternativeLimitsCurrentlyEnabled { get; }

    /// <summary>Gets a value indicating whether this activation owns its limits transition.</summary>
    public bool AlternativeLimitsOwned { get; }

    /// <summary>Gets the count of running torrents currently eligible for acquisition.</summary>
    public int? EligibleTorrentCount { get; }

    /// <summary>Gets the count carrying the active marker tag.</summary>
    public int? MarkedTorrentCount { get; }

    /// <summary>Gets the count both marked and stopped.</summary>
    public int? StoppedMarkedTorrentCount { get; }

    /// <summary>Gets the count carrying the never-touch tag.</summary>
    public int? ExcludedTorrentCount { get; }

    /// <summary>Gets the release countdown rounded up to whole seconds.</summary>
    public int? ReleaseGraceRemainingSeconds { get; }

    /// <summary>Gets a value indicating whether saved behavior waits for the next activation.</summary>
    public bool ConfigurationChangesPending { get; }

    /// <summary>Gets the saved configuration revision.</summary>
    public long CurrentConfigurationRevision { get; }

    /// <summary>Gets the active snapshotted revision.</summary>
    public long? ActivationConfigurationRevision { get; }

    /// <summary>Gets the last successful reconciliation instant.</summary>
    public DateTimeOffset? LastSuccessfulReconciliation { get; }

    /// <summary>Gets the current bounded failure.</summary>
    public JournalFailureCode? CurrentError { get; }

    /// <summary>Gets a value indicating whether marked-torrent recovery is applicable.</summary>
    public bool CanResumeMarkedTorrents { get; }

    /// <summary>Gets a value indicating whether prior limits state is known.</summary>
    public bool CanRestorePreviousSpeedSetting { get; }

    /// <summary>Gets a value indicating whether recovery can be cleared without qBittorrent mutation.</summary>
    public bool CanMarkResolved { get; }
}
