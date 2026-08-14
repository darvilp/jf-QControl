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
/// Provides administrator-only credential-safe configuration activation.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("QControl/Configuration")]
public sealed class QControlConfigurationController : ControllerBase
{
    private readonly ConfigurationService _configuration;

    /// <summary>Initializes a new instance of the <see cref="QControlConfigurationController"/> class.</summary>
    /// <param name="configuration">The validated configuration service.</param>
    public QControlConfigurationController(ConfigurationService configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <summary>Gets the current configuration without credential content.</summary>
    /// <returns>The complete credential-safe configuration.</returns>
    [HttpGet]
    [ProducesResponseType<ConfigurationView>(StatusCodes.Status200OK)]
    public ActionResult<ConfigurationView> Get() => Ok(_configuration.Get());

    /// <summary>Validates and conditionally activates one complete candidate.</summary>
    /// <param name="candidate">The complete candidate.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The accepted or bounded rejected result.</returns>
    [HttpPut]
    [ProducesResponseType<ConfigurationSaveResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConfigurationSaveResult>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ConfigurationSaveResult>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfigurationSaveResult>> SaveAsync(
        [FromBody] ConfigurationCandidate candidate,
        CancellationToken cancellationToken)
    {
        var result = await _configuration
            .SaveAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome switch
        {
            ConfigurationSaveOutcome.Accepted => Ok(result),
            ConfigurationSaveOutcome.Invalid => BadRequest(result),
            ConfigurationSaveOutcome.ConnectionFailed => BadRequest(result),
            ConfigurationSaveOutcome.RevisionConflict => Conflict(result),
            ConfigurationSaveOutcome.ActiveConnectionConflict => Conflict(result),
            _ => throw new InvalidOperationException("Unknown configuration save outcome."),
        };
    }
}
