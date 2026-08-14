using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Minimal filesystem port needed for atomic journal persistence.
/// </summary>
public interface IActivationJournalFileSystem
{
    /// <summary>
    /// Tests whether a path exists.
    /// </summary>
    /// <param name="path">The exact path.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Whether the file exists.</returns>
    ValueTask<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one size-bounded file.
    /// </summary>
    /// <param name="path">The exact path.</param>
    /// <param name="maximumBytes">The maximum accepted file length.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The complete file bytes.</returns>
    ValueTask<byte[]> ReadAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates, writes, and durably flushes a same-directory temporary file.
    /// </summary>
    /// <param name="temporaryPath">The new temporary path.</param>
    /// <param name="content">The complete serialized document.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing durable temporary-file completion.</returns>
    ValueTask WriteTemporaryAsync(
        string temporaryPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replaces the destination with the complete temporary file.
    /// </summary>
    /// <param name="temporaryPath">The completed same-directory temporary path.</param>
    /// <param name="destinationPath">The final journal path.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing replacement completion.</returns>
    ValueTask ReplaceAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a path when present.
    /// </summary>
    /// <param name="path">The exact path.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing deletion completion.</returns>
    ValueTask DeleteIfExistsAsync(string path, CancellationToken cancellationToken);
}
