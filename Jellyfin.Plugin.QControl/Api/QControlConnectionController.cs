using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.QControl.Api;

/// <summary>
/// Provides administrator-only read-only connection and category discovery.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("QControl/Connection")]
public sealed class QControlConnectionController : ControllerBase
{
    private readonly ConfigurationService _configuration;

    /// <summary>Initializes a new instance of the <see cref="QControlConnectionController"/> class.</summary>
    /// <param name="configuration">The candidate and saved-connection service.</param>
    public QControlConnectionController(ConfigurationService configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <summary>Tests a candidate through read-only qBittorrent endpoints.</summary>
    /// <param name="candidate">The complete candidate.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Compatible versions, categories, or a bounded failure.</returns>
    [HttpPost("Test")]
    [ProducesResponseType<QbittorrentConnectionProbeResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<QbittorrentConnectionProbeResult>> TestAsync(
        [FromBody] ConfigurationCandidate candidate,
        CancellationToken cancellationToken)
    {
        return Ok(await _configuration
            .TestConnectionAsync(candidate, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>Gets categories through the saved read-only connection.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Compatible versions, categories, or a bounded failure.</returns>
    [HttpGet("Categories")]
    [ProducesResponseType<QbittorrentConnectionProbeResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<QbittorrentConnectionProbeResult>> GetCategoriesAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await _configuration
            .GetCategoriesAsync(cancellationToken)
            .ConfigureAwait(false));
    }
}
