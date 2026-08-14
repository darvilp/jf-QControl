using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.QControl.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests;

public sealed class PluginContractTests
{
    [Fact]
    public void JellyfinDiscoversPermanentIdentityAndEmbeddedAdministratorPage()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Jellyfin.Plugin.QControl.dll");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var pluginType = assembly.GetType("Jellyfin.Plugin.QControl.Plugin");

        Assert.NotNull(pluginType);

        var applicationPaths = new Mock<IApplicationPaths>(MockBehavior.Strict);
        applicationPaths.SetupGet(paths => paths.PluginsPath).Returns("/tmp/jellyfin/plugins");
        var instance = Activator.CreateInstance(
            pluginType,
            applicationPaths.Object,
            Mock.Of<IXmlSerializer>());

        Assert.NotNull(instance);
        var plugin = Assert.IsAssignableFrom<IPlugin>(instance);
        Assert.Equal("QControl", plugin.Name);
        Assert.Equal(new Guid("ab18c878-1856-4853-8f21-5028a1d5a7b2"), plugin.Id);

        var page = Assert.Single(Assert.IsAssignableFrom<IHasWebPages>(instance).GetPages());
        Assert.Equal("QControl", page.Name);
        Assert.Equal(
            "Jellyfin.Plugin.QControl.Configuration.configPage.html",
            page.EmbeddedResourcePath);

        using var resource = assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
        Assert.NotNull(resource);

        var typedPlugin = Assert.IsType<Jellyfin.Plugin.QControl.Plugin>(instance);
        Assert.Throws<InvalidOperationException>(() =>
            typedPlugin.UpdateConfiguration(new PluginConfiguration()));
    }
}
