using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Performs a read-only compatibility and category probe for a complete candidate.
/// </summary>
public interface IQbittorrentConnectionProbe
{
    /// <summary>Probes the candidate without changing qBittorrent.</summary>
    /// <param name="candidate">A complete in-memory configuration including resolved credential.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A privacy-safe bounded result.</returns>
    Task<QbittorrentConnectionProbeResult> ProbeAsync(
        PluginConfiguration candidate,
        CancellationToken cancellationToken);
}
