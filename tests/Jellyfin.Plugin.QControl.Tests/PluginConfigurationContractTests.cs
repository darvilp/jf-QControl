using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.QControl.Configuration;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests;

public sealed class PluginConfigurationContractTests
{
    [Fact]
    public void FreshConfigurationUsesNamespacedTagPolicy()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal("qcontrol-resume", configuration.MarkerTag);
        Assert.Equal(["qcontrol-ignore"], configuration.ExclusionTags);
    }

    [Fact]
    public void DefaultConfigurationRoundTripsItsSchemaVersion()
    {
        var schemaVersion = typeof(PluginConfiguration).GetProperty("SchemaVersion");

        Assert.NotNull(schemaVersion);

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var document = new MemoryStream();
        serializer.Serialize(document, new PluginConfiguration());
        document.Position = 0;
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(document, readerSettings);

        var roundTripped = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));
        Assert.Equal(1, schemaVersion.GetValue(roundTripped));
    }

    [Fact]
    public void StoredApiKeyPersistsToHostConfigurationButNotJsonReads()
    {
        const string apiKey = "qbt_0123456789abcdefghijklmnopqr";
        var configuration = new PluginConfiguration
        {
            QbittorrentApiKey = apiKey,
        };
        var xmlSerializer = new XmlSerializer(typeof(PluginConfiguration));
        using var document = new MemoryStream();

        xmlSerializer.Serialize(document, configuration);
        var persistedXml = System.Text.Encoding.UTF8.GetString(document.ToArray());
        var configurationRead = JsonSerializer.Serialize(configuration);

        Assert.Contains(apiKey, persistedXml, System.StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, configurationRead, System.StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(PluginConfiguration.QbittorrentApiKey),
            configurationRead,
            System.StringComparison.OrdinalIgnoreCase);
    }
}
