namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Reads and persists complete accepted Jellyfin plugin configuration.
/// </summary>
public interface IPluginConfigurationPersistence
{
    /// <summary>Gets the current configuration.</summary>
    PluginConfiguration Current { get; }

    /// <summary>Persists and activates a server-validated configuration.</summary>
    /// <param name="configuration">The accepted configuration.</param>
    void Save(PluginConfiguration configuration);
}
