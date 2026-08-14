using System;

namespace Jellyfin.Plugin.QControl.Domain.Activation;

/// <summary>
/// Caller-supplied identity and configuration for a possible new activation.
/// </summary>
/// <param name="ActivationId">The unique activation identifier.</param>
/// <param name="ConfigurationRevision">The behavior configuration revision to snapshot.</param>
/// <param name="ReleaseGrace">The complete absence interval required before restoration.</param>
public sealed record ActivationRequest(
    Guid ActivationId,
    long ConfigurationRevision,
    TimeSpan ReleaseGrace);
