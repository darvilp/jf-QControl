using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Recovery;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.QControl.Api;

/// <summary>
/// Provides administrator-only explicit interruption recovery commands.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("QControl/Recovery")]
public sealed class QControlRecoveryController : ControllerBase
{
    private readonly RecoveryService _recovery;

    /// <summary>Initializes a new instance of the <see cref="QControlRecoveryController"/> class.</summary>
    /// <param name="recovery">The serialized explicit recovery service.</param>
    public QControlRecoveryController(RecoveryService recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        _recovery = recovery;
    }

    /// <summary>Starts marked torrents and removes accepted markers.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The bounded recovery result.</returns>
    [HttpPost("ResumeMarkedTorrents")]
    public async Task<ActionResult<RecoveryResult>> ResumeMarkedTorrentsAsync(
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _recovery
            .ResumeMarkedTorrentsAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>Restores the previously observed Alternative Limits mode.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The bounded recovery result.</returns>
    [HttpPost("RestorePreviousSpeedSetting")]
    public async Task<ActionResult<RecoveryResult>> RestorePreviousSpeedSettingAsync(
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _recovery
            .RestorePreviousSpeedSettingAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>Clears recovery state without qBittorrent mutation.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The bounded recovery result.</returns>
    [HttpPost("MarkResolved")]
    public async Task<ActionResult<RecoveryResult>> MarkResolvedAsync(
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _recovery
            .MarkResolvedAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    private ActionResult<RecoveryResult> ToActionResult(RecoveryResult result)
    {
        return result.Outcome switch
        {
            RecoveryOutcome.Completed => Ok(result),
            RecoveryOutcome.NotAvailable => Conflict(result),
            RecoveryOutcome.Failed => StatusCode(StatusCodes.Status502BadGateway, result),
            _ => throw new InvalidOperationException("Unknown recovery outcome."),
        };
    }
}
