using System;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
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
        serviceCollection.AddSingleton<IPlaybackSessionSource, JellyfinPlaybackSessionSource>();
        serviceCollection.AddSingleton<IReconciliationDelay, TimeProviderReconciliationDelay>();
        serviceCollection.AddSingleton<IActivationJournalFileSystem, PhysicalActivationJournalFileSystem>();
        serviceCollection.AddSingleton<IActivationJournalStore>(serviceProvider =>
            new ActivationJournalStore(
                ActivationJournalPathResolver.Resolve(
                    serviceProvider.GetRequiredService<IApplicationPaths>()),
                serviceProvider.GetRequiredService<IActivationJournalFileSystem>()));
        serviceCollection.AddSingleton<IActivationJournalFactory, UnconfiguredActivationJournalFactory>();
        serviceCollection.AddSingleton<IProtectionActionSet, UnconfiguredProtectionActionSet>();
        serviceCollection.AddSingleton<IProtectionCoordinator>(serviceProvider =>
            new ProtectionCoordinator(
                serviceProvider.GetRequiredService<IPlaybackSessionSource>(),
                serviceProvider.GetRequiredService<IActivationJournalFactory>(),
                serviceProvider.GetRequiredService<IProtectionActionSet>(),
                serviceProvider.GetRequiredService<IActivationJournalStore>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                Guid.NewGuid()));
        serviceCollection.AddSingleton<ProtectionCoordinatorWorker>();
        serviceCollection.AddSingleton<IProtectionWakeSignal>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinatorWorker>());
        serviceCollection.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<ProtectionCoordinatorWorker>());
        serviceCollection.AddHostedService<JellyfinPlaybackEventObserver>();
    }
}
