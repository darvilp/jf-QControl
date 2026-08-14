using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.QControl.Configuration;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests;

public sealed class PluginConfigurationContractTests
{
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
}
