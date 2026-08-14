using System;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;
using Jellyfin.Plugin.QControl.Recovery;
using Jellyfin.Plugin.QControl.Status;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.QControl;

/// <summary>
/// Registers QControl's hosted playback coordination seams with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(applicationHost);

        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(ProcessInstanceIdentity.Create());
        serviceCollection.AddSingleton<IPlaybackSessionSource, JellyfinPlaybackSessionSource>();
        serviceCollection.AddSingleton<IReconciliationDelay, TimeProviderReconciliationDelay>();
        serviceCollection.AddSingleton<IActivationJournalFileSystem, PhysicalActivationJournalFileSystem>();
        serviceCollection.AddSingleton<IActivationJournalStore>(serviceProvider =>
            new ActivationJournalStore(
                ActivationJournalPathResolver.Resolve(
                    serviceProvider.GetRequiredService<IApplicationPaths>()),
                serviceProvider.GetRequiredService<IActivationJournalFileSystem>()));
        serviceCollection.AddSingleton<IPluginConfigurationPersistence, PluginConfigurationPersistence>();
        serviceCollection.AddSingleton<IActivationStateReader, JournalActivationStateReader>();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<IQbittorrentClientFactory, ConfiguredQbittorrentClientFactory>();
        serviceCollection.AddSingleton<IQbittorrentConnectionProbe, QbittorrentConnectionProbe>();
        serviceCollection.AddSingleton<IActivationJournalFactory, ConfiguredActivationJournalFactory>();
        serviceCollection.AddSingleton<IProtectionActionSet, ConfiguredProtectionActionSet>();
        serviceCollection.AddSingleton<IProtectionExecutionGate, ProtectionExecutionGate>();
        serviceCollection.AddSingleton<ProtectionCoordinator>(serviceProvider =>
            new ProtectionCoordinator(
                serviceProvider.GetRequiredService<IPlaybackSessionSource>(),
                serviceProvider.GetRequiredService<IActivationJournalFactory>(),
                serviceProvider.GetRequiredService<IProtectionActionSet>(),
                serviceProvider.GetRequiredService<IActivationJournalStore>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ProcessInstanceIdentity>().Value,
                serviceProvider.GetRequiredService<IProtectionExecutionGate>()));
        serviceCollection.AddSingleton<IProtectionCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinator>());
        serviceCollection.AddSingleton<IProtectionCoordinatorStateControl>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinator>());
        serviceCollection.AddSingleton<ConfigurationService>();
        serviceCollection.AddSingleton<OperationalStatusService>();
        serviceCollection.AddSingleton<RecoveryService>();
        serviceCollection.AddSingleton<ProtectionCoordinatorWorker>();
        serviceCollection.AddSingleton<IProtectionWakeSignal>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinatorWorker>());
        serviceCollection.AddHostedService<ConfigurationMigrationInitializer>();
        serviceCollection.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinatorWorker>());
        serviceCollection.AddHostedService<JellyfinPlaybackEventObserver>();
    }
}
