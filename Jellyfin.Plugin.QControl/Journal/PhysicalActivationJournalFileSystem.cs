using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Native filesystem implementation using same-directory atomic replacement.
/// </summary>
public sealed class PhysicalActivationJournalFileSystem : IActivationJournalFileSystem
{
    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(File.Exists(path));
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> ReadAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, int.MaxValue - 1);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[maximumBytes + 1];
            var totalBytes = 0;
            while (totalBytes < buffer.Length)
            {
                var bytesRead = await stream
                    .ReadAsync(buffer.AsMemory(totalBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return buffer.AsSpan(0, totalBytes).ToArray();
                }

                totalBytes += bytesRead;
            }

            throw new IOException("The activation journal exceeds its size bound.");
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteTemporaryAsync(
        string temporaryPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using (stream.ConfigureAwait(false))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask ReplaceAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporaryPath, destinationPath, overwrite: true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return ValueTask.CompletedTask;
    }
}
