using System;
using System.Linq;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Playback;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Coordination;

public sealed class PluginServiceRegistratorTests
{
    [Fact]
    public void RuntimeGraphResolvesOneSharedWorkerWithoutConfiguredQbittorrent()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(value => value.PluginConfigurationsPath).Returns("/tmp/qcontrol-config");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(paths.Object);
        services.AddSingleton(Mock.Of<ISessionManager>());
        new PluginServiceRegistrator().RegisterServices(
            services,
            Mock.Of<IServerApplicationHost>());
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var worker = provider.GetRequiredService<ProtectionCoordinatorWorker>();
        var wakeSignal = provider.GetRequiredService<IProtectionWakeSignal>();

        Assert.Same(worker, wakeSignal);
        Assert.Contains(worker, hostedServices);
        Assert.Single(hostedServices.OfType<JellyfinPlaybackEventObserver>());
        Assert.IsType<JellyfinPlaybackSessionSource>(
            provider.GetRequiredService<IPlaybackSessionSource>());
        Assert.IsType<ConfiguredActivationJournalFactory>(
            provider.GetRequiredService<IActivationJournalFactory>());
    }
}
