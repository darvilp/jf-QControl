using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Journal;

public sealed class ActivationJournalRoundTripTests
{
    private static readonly Guid ProcessInstanceId =
        new("d30a31f6-e24c-4a04-a394-7ec61b19cc85");

    [Fact]
    public async Task ValidVersionOneJournalRoundTripsWithFullSameProcessAuthority()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = ActivationJournalPathResolver.Resolve(directory);
            var store = new ActivationJournalStore(
                path,
                new PhysicalActivationJournalFileSystem());
            var expected = JournalTestData.Create(ProcessInstanceId);

            await store.WriteAsync(expected, CancellationToken.None).ConfigureAwait(true);
            var result = await store
                .LoadAsync(ProcessInstanceId, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Equal(ActivationJournalLoadStatus.Active, result.Status);
            Assert.Equal(ActivationJournalAuthority.Full, result.Authority);
            var actual = Assert.IsType<ActivationJournalDocument>(result.Document);
            Assert.Equal(expected.ActivationId, actual.ActivationId);
            Assert.Equal(expected.StartedAt, actual.StartedAt);
            Assert.Equal(expected.SessionIds.ToArray(), actual.SessionIds.ToArray());
            Assert.Equal(expected.Configuration.Revision, actual.Configuration.Revision);
            Assert.Equal(
                expected.Configuration.AlternativeLimitsEnabled,
                actual.Configuration.AlternativeLimitsEnabled);
            Assert.Equal(
                expected.Configuration.StopTorrentsEnabled,
                actual.Configuration.StopTorrentsEnabled);
            Assert.Equal(expected.Configuration.StopScope, actual.Configuration.StopScope);
            Assert.Equal(
                expected.Configuration.SelectedCategories.ToArray(),
                actual.Configuration.SelectedCategories.ToArray());
            Assert.Equal(
                expected.Configuration.IncludeIncomplete,
                actual.Configuration.IncludeIncomplete);
            Assert.Equal(expected.Configuration.IncludeCompleted, actual.Configuration.IncludeCompleted);
            Assert.Equal(expected.Configuration.MarkerTag, actual.Configuration.MarkerTag);
            Assert.Equal(expected.Configuration.NeverTouchTag, actual.Configuration.NeverTouchTag);
            Assert.Equal(expected.Configuration.ReleaseGrace, actual.Configuration.ReleaseGrace);
            Assert.Equal(expected.Endpoint, actual.Endpoint);
            Assert.Equal(expected.AlternativeLimits, actual.AlternativeLimits);
            Assert.Equal(expected.Torrents.ToArray(), actual.Torrents.ToArray());
            Assert.Equal(expected.Phase, actual.Phase);
            Assert.Equal(expected.ReleaseDueAt, actual.ReleaseDueAt);
            Assert.Equal(expected.LastSuccessfulReconciliation, actual.LastSuccessfulReconciliation);
            Assert.Equal(expected.LastFailure, actual.LastFailure);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(path));
            }

            await store.DeleteAsync(CancellationToken.None).ConfigureAwait(true);
            var deleted = await store
                .LoadAsync(ProcessInstanceId, CancellationToken.None)
                .ConfigureAwait(true);
            Assert.Equal(ActivationJournalLoadStatus.Missing, deleted.Status);
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DifferentProcessLoadsValidJournalAsProtectOnlyRecovery()
    {
        var fileSystem = new InMemoryActivationJournalFileSystem();
        var store = new ActivationJournalStore("/runtime/QControl.journal.json", fileSystem);
        await store
            .WriteAsync(JournalTestData.Create(ProcessInstanceId), CancellationToken.None)
            .ConfigureAwait(true);

        var result = await store
            .LoadAsync(Guid.NewGuid(), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(ActivationJournalLoadStatus.Interrupted, result.Status);
        Assert.Equal(ActivationJournalAuthority.ProtectOnly, result.Authority);
        Assert.NotNull(result.Document);
    }

    [Theory]
    [InlineData(
        "/config/plugins/configurations",
        "/config/plugins/configurations/Jellyfin.Plugin.QControl.journal.json")]
    [InlineData(
        "C:\\ProgramData\\Jellyfin\\Server\\plugins\\configurations",
        "C:\\ProgramData\\Jellyfin\\Server\\plugins\\configurations\\Jellyfin.Plugin.QControl.journal.json")]
    [InlineData(
        "\\\\server\\jellyfin\\plugins\\configurations\\",
        "\\\\server\\jellyfin\\plugins\\configurations\\Jellyfin.Plugin.QControl.journal.json")]
    public void ResolverConstructsNativeUnixAndWindowsPaths(string directory, string expected)
    {
        Assert.Equal(expected, ActivationJournalPathResolver.Resolve(directory));
    }

    [Fact]
    public void ResolverUsesOnlyJellyfinPluginConfigurationDirectory()
    {
        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(paths => paths.PluginConfigurationsPath)
            .Returns("/runtime/plugins/configurations");
        applicationPaths.SetupGet(paths => paths.PluginsPath)
            .Returns("/runtime/plugins/binaries");
        applicationPaths.SetupGet(paths => paths.CachePath)
            .Returns("/runtime/cache");

        var resolved = ActivationJournalPathResolver.Resolve(applicationPaths.Object);

        Assert.Equal(
            "/runtime/plugins/configurations/Jellyfin.Plugin.QControl.journal.json",
            resolved);
        Assert.DoesNotContain("binaries", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("cache", resolved, StringComparison.Ordinal);
    }

    private sealed class InMemoryActivationJournalFileSystem : IActivationJournalFileSystem
    {
        private readonly System.Collections.Generic.Dictionary<string, byte[]> _files = [];

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
            if (content.Length > maximumBytes)
            {
                throw new InvalidOperationException("Fixture exceeded the journal bound.");
            }

            return ValueTask.FromResult((byte[])content.Clone());
        }

        public ValueTask WriteTemporaryAsync(
            string temporaryPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files[temporaryPath] = content.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceAsync(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files[destinationPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.Remove(path);
            return ValueTask.CompletedTask;
        }
    }
}
