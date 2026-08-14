using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Journal;

public sealed class ActivationJournalAtomicWriteTests
{
    private const string JournalPath = "/runtime/Jellyfin.Plugin.QControl.journal.json";
    private static readonly Guid ProcessInstanceId =
        new("f071b4ec-200d-4d67-bdf6-4be8d8a5963a");

    [Fact]
    public async Task ReplacementWritesCompleteTemporaryFileBeforeAtomicMove()
    {
        var fileSystem = new FaultInjectingFileSystem();
        var store = new ActivationJournalStore(JournalPath, fileSystem);
        var original = JournalTestData.Create(ProcessInstanceId);
        await store.WriteAsync(original, CancellationToken.None).ConfigureAwait(true);
        fileSystem.Operations.Clear();
        var replacement = original with { ActivationId = Guid.NewGuid() };
        fileSystem.AfterTemporaryWrite = temporaryPath =>
        {
            Assert.Equal(
                Path.GetDirectoryName(JournalPath),
                Path.GetDirectoryName(temporaryPath));
            Assert.Contains(
                original.ActivationId.ToString(),
                fileSystem.ReadText(JournalPath),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                replacement.ActivationId.ToString(),
                fileSystem.ReadText(temporaryPath),
                StringComparison.OrdinalIgnoreCase);
        };

        await store.WriteAsync(replacement, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(["write-temporary", "replace", "delete-temporary"], fileSystem.Operations);
        Assert.Contains(
            replacement.ActivationId.ToString(),
            fileSystem.ReadText(JournalPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(fileSystem.Paths);
    }

    [Theory]
    [InlineData((int)FailurePoint.WriteTemporary)]
    [InlineData((int)FailurePoint.Replace)]
    public async Task FailedReplacementLeavesPriorValidJournalReadable(int failureValue)
    {
        var failurePoint = (FailurePoint)failureValue;
        var fileSystem = new FaultInjectingFileSystem();
        var store = new ActivationJournalStore(JournalPath, fileSystem);
        var original = JournalTestData.Create(ProcessInstanceId);
        await store.WriteAsync(original, CancellationToken.None).ConfigureAwait(true);
        fileSystem.Operations.Clear();
        fileSystem.Failure = failurePoint;

        var exception = await Assert.ThrowsAsync<ActivationJournalException>(
            () => store.WriteAsync(
                original with { ActivationId = Guid.NewGuid() },
                CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("qbt_SECRET", exception.ToString(), StringComparison.Ordinal);
        fileSystem.Failure = FailurePoint.None;
        var loaded = await store
            .LoadAsync(ProcessInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Active, loaded.Status);
        Assert.Equal(original.ActivationId, loaded.Document?.ActivationId);
        Assert.DoesNotContain("replace", fileSystem.Operations.FindAll(
            operation => failurePoint == FailurePoint.WriteTemporary && operation == "replace"));
        Assert.Single(fileSystem.Paths);
    }

    private enum FailurePoint
    {
        None,
        WriteTemporary,
        Replace,
    }

    private sealed class FaultInjectingFileSystem : IActivationJournalFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public List<string> Operations { get; } = [];

        public IEnumerable<string> Paths => _files.Keys;

        public FailurePoint Failure { get; set; }

        public Action<string>? AfterTemporaryWrite { get; set; }

        public string ReadText(string path)
        {
            return Encoding.UTF8.GetString(_files[path]);
        }

        public ValueTask<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_files.ContainsKey(path));
        }

        public ValueTask<byte[]> ReadAsync(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = _files[path];
            Assert.True(content.Length <= maximumBytes);
            return ValueTask.FromResult((byte[])content.Clone());
        }

        public ValueTask WriteTemporaryAsync(
            string temporaryPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("write-temporary");
            if (Failure == FailurePoint.WriteTemporary)
            {
                throw new IOException("Injected temporary-write qbt_SECRET failure.");
            }

            _files[temporaryPath] = content.ToArray();
            AfterTemporaryWrite?.Invoke(temporaryPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceAsync(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("replace");
            if (Failure == FailurePoint.Replace)
            {
                throw new IOException("Injected replace qbt_SECRET failure.");
            }

            _files[destinationPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(path, JournalPath, StringComparison.Ordinal))
            {
                Operations.Add("delete-temporary");
            }

            _files.Remove(path);
            return ValueTask.CompletedTask;
        }
    }
}
