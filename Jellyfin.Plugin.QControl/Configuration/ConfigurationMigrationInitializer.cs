using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Persists the supported pre-alpha configuration migration before coordination starts.
/// </summary>
public sealed class ConfigurationMigrationInitializer : IHostedService
{
    private readonly IPluginConfigurationPersistence _persistence;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationMigrationInitializer"/> class.
    /// </summary>
    /// <param name="persistence">The loaded Jellyfin configuration boundary.</param>
    public ConfigurationMigrationInitializer(IPluginConfigurationPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        _persistence = persistence;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = PluginConfigurationMigrator.Normalize(
            _persistence.Current,
            out var changed);
        if (changed)
        {
            _persistence.Save(normalized);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
