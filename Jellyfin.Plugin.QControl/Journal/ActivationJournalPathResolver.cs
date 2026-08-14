using System;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Resolves the journal beside Jellyfin plugin configuration on Unix and Windows.
/// </summary>
public static class ActivationJournalPathResolver
{
    /// <summary>
    /// The fixed journal file name.
    /// </summary>
    public const string JournalFileName = "Jellyfin.Plugin.QControl.journal.json";

    /// <summary>
    /// Resolves from Jellyfin's runtime paths.
    /// </summary>
    /// <param name="applicationPaths">The active Jellyfin application paths.</param>
    /// <returns>The runtime journal path.</returns>
    public static string Resolve(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        return Resolve(applicationPaths.PluginConfigurationsPath);
    }

    /// <summary>
    /// Resolves from an absolute Unix, drive-letter Windows, or UNC directory.
    /// </summary>
    /// <param name="pluginConfigurationsPath">Jellyfin's plugin configuration directory.</param>
    /// <returns>The runtime journal path.</returns>
    public static string Resolve(string pluginConfigurationsPath)
    {
        if (string.IsNullOrWhiteSpace(pluginConfigurationsPath))
        {
            throw new ArgumentException(
                "Jellyfin's plugin configuration directory is required.",
                nameof(pluginConfigurationsPath));
        }

        var isDrivePath = pluginConfigurationsPath.Length >= 3
            && char.IsAsciiLetter(pluginConfigurationsPath[0])
            && pluginConfigurationsPath[1] == ':'
            && (pluginConfigurationsPath[2] == '\\' || pluginConfigurationsPath[2] == '/');
        var isUncPath = pluginConfigurationsPath.StartsWith("\\\\", StringComparison.Ordinal);
        var isUnixPath = pluginConfigurationsPath.StartsWith('/');
        if (!isDrivePath && !isUncPath && !isUnixPath)
        {
            throw new ArgumentException(
                "Jellyfin's plugin configuration directory must be absolute.",
                nameof(pluginConfigurationsPath));
        }

        var separator = isDrivePath || isUncPath ? '\\' : '/';
        return string.Concat(
            pluginConfigurationsPath.TrimEnd('/', '\\'),
            separator,
            JournalFileName);
    }
}
