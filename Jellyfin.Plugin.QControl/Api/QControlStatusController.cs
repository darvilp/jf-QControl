using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Status;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.QControl.Api;

/// <summary>
/// Provides administrator-only privacy-safe operational status.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("QControl/Status")]
public sealed class QControlStatusController : ControllerBase
{
    private readonly OperationalStatusService _status;

    /// <summary>Initializes a new instance of the <see cref="QControlStatusController"/> class.</summary>
    /// <param name="status">The privacy-safe status service.</param>
    public QControlStatusController(OperationalStatusService status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
    }

    /// <summary>Gets current operational status.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The complete privacy-safe status.</returns>
    [HttpGet]
    [ProducesResponseType<OperationalStatusSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalStatusSnapshot>> GetAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await _status.GetAsync(cancellationToken).ConfigureAwait(false));
    }
}
