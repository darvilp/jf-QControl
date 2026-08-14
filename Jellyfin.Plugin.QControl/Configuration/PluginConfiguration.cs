using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Persisted QControl configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the persisted configuration schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
