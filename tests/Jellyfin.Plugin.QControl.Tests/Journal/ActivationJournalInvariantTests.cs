using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Journal;

public sealed class ActivationJournalInvariantTests
{
    private const string JournalPath = "/runtime/Jellyfin.Plugin.QControl.journal.json";
    private static readonly Guid ProcessInstanceId =
        new("69791e92-a1e1-4c78-bc83-c63b11dddfc8");

    [Fact]
    public async Task StopIntentRequiresConfirmedMarkerPresence()
    {
        var journal = JournalTestData.Create(ProcessInstanceId) with
        {
            Torrents = ImmutableArray.Create(new TorrentMutationJournalEntry(
                "aaaaaaaa",
                MarkerAddStage: JournalMutationStage.IntentPersisted,
                StopStage: JournalMutationStage.IntentPersisted,
                StartStage: JournalMutationStage.None,
                MarkerRemoveStage: JournalMutationStage.None)),
        };
        var store = new ActivationJournalStore(JournalPath, new RawFileSystem());

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(journal, CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task MarkerRemovalRequiresConfirmedAcceptedStart()
    {
        var journal = JournalTestData.Create(ProcessInstanceId) with
        {
            Torrents = ImmutableArray.Create(new TorrentMutationJournalEntry(
                "aaaaaaaa",
                MarkerAddStage: JournalMutationStage.Confirmed,
                StopStage: JournalMutationStage.Confirmed,
                StartStage: JournalMutationStage.IntentPersisted,
                MarkerRemoveStage: JournalMutationStage.IntentPersisted)),
        };
        var store = new ActivationJournalStore(JournalPath, new RawFileSystem());

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(journal, CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task AlternativeLimitsOwnershipRequiresConfirmedDisabledToEnabledTransition()
    {
        var journal = JournalTestData.Create(ProcessInstanceId) with
        {
            AlternativeLimits = new AlternativeLimitsJournalState(
                InitialEnabled: true,
                EnabledByActivation: true,
                EnableStage: JournalMutationStage.Confirmed,
                DisableStage: JournalMutationStage.None),
        };
        var store = new ActivationJournalStore(JournalPath, new RawFileSystem());

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(journal, CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task UndefinedMutationStageInSerializedVersionOneIsCorrupt()
    {
        var fileSystem = new RawFileSystem();
        var store = new ActivationJournalStore(JournalPath, fileSystem);
        await store
            .WriteAsync(JournalTestData.Create(ProcessInstanceId), CancellationToken.None)
            .ConfigureAwait(true);
        var json = Encoding.UTF8.GetString(fileSystem.FinalContent!);
        fileSystem.FinalContent = Encoding.UTF8.GetBytes(json.Replace(
            "\"stopStage\": \"intentPersisted\"",
            "\"stopStage\": 999",
            StringComparison.Ordinal));

        var result = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Corrupt, result.Status);
        Assert.Equal(ActivationJournalAuthority.None, result.Authority);
    }

    [Fact]
    public async Task QbittorrentCredentialMaterialIsRejectedEvenInAllowedStringField()
    {
        var original = JournalTestData.Create(ProcessInstanceId);
        var journal = original with
        {
            Configuration = original.Configuration with
            {
                MarkerTag = "qbt_0123456789abcdefghijklmnopqr",
            },
        };
        var store = new ActivationJournalStore(JournalPath, new RawFileSystem());

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(journal, CancellationToken.None).AsTask()).ConfigureAwait(true);
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
