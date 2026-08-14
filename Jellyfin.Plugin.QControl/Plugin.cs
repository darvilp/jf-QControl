using System;
using System.Collections.Generic;
using Jellyfin.Plugin.QControl.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.QControl;

/// <summary>
/// The QControl Jellyfin plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="xmlSerializer">The Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the loaded plugin instance.
    /// </summary>
    public static Plugin Instance { get; private set; } = null!;

    /// <inheritdoc />
    public override string Name => "QControl";

    /// <inheritdoc />
    public override string Description =>
        "Reduces qBittorrent activity while Jellyfin playback is present.";

    /// <inheritdoc />
    public override Guid Id => new("ab18c878-1856-4853-8f21-5028a1d5a7b2");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = "Jellyfin.Plugin.QControl.Configuration.configPage.html",
        };
    }

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        throw new InvalidOperationException(
            "QControl configuration must use the validated administrator endpoint.");
    }

    /// <summary>
    /// Persists and activates one server-validated configuration.
    /// </summary>
    /// <param name="configuration">The complete accepted configuration.</param>
    internal void ActivateValidatedConfiguration(PluginConfiguration configuration)
    {
        SaveConfiguration(configuration);
        Configuration = configuration;
        ConfigurationChanged?.Invoke(this, configuration);
    }
}
