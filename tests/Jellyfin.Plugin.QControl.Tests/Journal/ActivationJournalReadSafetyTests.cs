using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Journal;

public sealed class ActivationJournalReadSafetyTests
{
    private const string JournalPath = "/runtime/Jellyfin.Plugin.QControl.journal.json";
    private static readonly Guid ProcessInstanceId =
        new("87129fae-d5fe-4403-9613-80d734a3e080");

    [Fact]
    public async Task MissingJournalCarriesNoAutomaticAuthority()
    {
        var store = new ActivationJournalStore(JournalPath, new RawFileSystem());

        var result = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Missing, result.Status);
        Assert.Equal(ActivationJournalAuthority.None, result.Authority);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1")]
    [InlineData("{\"schemaVersion\":1,\"processInstanceId\":null}")]
    public async Task TruncatedOrInvalidVersionOneJournalCarriesNoAuthority(string json)
    {
        var fileSystem = new RawFileSystem { FinalContent = Encoding.UTF8.GetBytes(json) };
        var store = new ActivationJournalStore(JournalPath, fileSystem);

        var result = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Corrupt, result.Status);
        Assert.Equal(ActivationJournalAuthority.None, result.Authority);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task UnsupportedSchemaIsExplicitAndCarriesNoAuthority(int schemaVersion)
    {
        var json = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"schemaVersion\":{schemaVersion}}}");
        var fileSystem = new RawFileSystem { FinalContent = Encoding.UTF8.GetBytes(json) };
        var store = new ActivationJournalStore(JournalPath, fileSystem);

        var result = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.UnsupportedSchema, result.Status);
        Assert.Equal(ActivationJournalAuthority.None, result.Authority);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task UnknownSecretBearingPropertyMakesVersionOneJournalCorrupt()
    {
        const string secret = "qbt_SECRET_MUST_NOT_BE_ACCEPTED";
        var fileSystem = new RawFileSystem();
        var store = new ActivationJournalStore(JournalPath, fileSystem);
        await store
            .WriteAsync(JournalTestData.Create(ProcessInstanceId), CancellationToken.None)
            .ConfigureAwait(true);
        var validJson = Encoding.UTF8.GetString(fileSystem.FinalContent!);
        fileSystem.FinalContent = Encoding.UTF8.GetBytes(string.Concat(
            "{\"apiKey\":\"",
            secret,
            "\",",
            validJson[1..]));

        var result = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Corrupt, result.Status);
        Assert.Equal(ActivationJournalAuthority.None, result.Authority);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task SerializedSchemaContainsOnlyPrivacyReviewedFields()
    {
        var fileSystem = new RawFileSystem();
        var store = new ActivationJournalStore(JournalPath, fileSystem);
        await store
            .WriteAsync(JournalTestData.Create(ProcessInstanceId), CancellationToken.None)
            .ConfigureAwait(true);
        using var json = JsonDocument.Parse(fileSystem.FinalContent!);
        var propertyNames = new SortedSet<string>(StringComparer.Ordinal);

        CollectPropertyNames(json.RootElement, propertyNames);

        Assert.Equal(
            [
                "activationId",
                "alternativeLimits",
                "alternativeLimitsEnabled",
                "basePath",
                "configuration",
                "disableStage",
                "enabledByActivation",
                "enableStage",
                "endpoint",
                "hash",
                "host",
                "includeCompleted",
                "includeIncomplete",
                "initialEnabled",
                "lastFailure",
                "lastSuccessfulReconciliation",
                "manualRestoreStage",
                "manualRestoreTarget",
                "markerAddStage",
                "markerRemoveStage",
                "markerTag",
                "neverTouchTag",
                "phase",
                "port",
                "processInstanceId",
                "releaseDueAt",
                "releaseGrace",
                "revision",
                "schemaVersion",
                "scheme",
                "selectedCategories",
                "sessionIds",
                "startedAt",
                "startStage",
                "stopScope",
                "stopStage",
                "stopTorrentsEnabled",
                "torrents",
            ],
            propertyNames);
        var serialized = Encoding.UTF8.GetString(fileSystem.FinalContent!);
        string[] forbidden =
        [
            "apiKey",
            "authorization",
            "username",
            "mediaTitle",
            "torrentName",
            "tracker",
            "filePath",
        ];
        Assert.DoesNotContain(
            forbidden,
            value => serialized.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectPropertyNames(JsonElement element, ISet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                CollectPropertyNames(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectPropertyNames(item, names);
            }
        }
    }

    private sealed class RawFileSystem : IActivationJournalFileSystem
    {
        private readonly Dictionary<string, byte[]> _temporaryFiles = [];

        public byte[]? FinalContent { get; set; }

        public ValueTask<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(FinalContent is not null);
        }

        public ValueTask<byte[]> ReadAsync(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotNull(FinalContent);
            Assert.True(FinalContent.Length <= maximumBytes);
            return ValueTask.FromResult((byte[])FinalContent.Clone());
        }

        public ValueTask WriteTemporaryAsync(
            string temporaryPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _temporaryFiles[temporaryPath] = content.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceAsync(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FinalContent = _temporaryFiles[temporaryPath];
            _temporaryFiles.Remove(temporaryPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(path, JournalPath, StringComparison.Ordinal))
            {
                FinalContent = null;
            }
            else
            {
                _temporaryFiles.Remove(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
