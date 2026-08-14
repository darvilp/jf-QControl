using System;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Persists accepted configuration through the loaded Jellyfin plugin instance.
/// </summary>
public sealed class PluginConfigurationPersistence : IPluginConfigurationPersistence
{
    /// <inheritdoc />
    public PluginConfiguration Current => Plugin.Instance.Configuration;

    /// <inheritdoc />
    public void Save(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Plugin.Instance.ActivateValidatedConfiguration(configuration);
    }
}
