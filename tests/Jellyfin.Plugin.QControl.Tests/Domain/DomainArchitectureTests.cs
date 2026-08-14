using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class DomainArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Jellyfin.",
        "MediaBrowser.",
        "Microsoft.AspNetCore.",
        "Microsoft.Extensions.",
        "System.IO.",
        "System.Net.",
    ];

    [Fact]
    public void DomainAssemblyHasNoFrameworkOrBoundaryDependencies()
    {
        var referencedAssemblies = typeof(PlaybackPresence).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            referencedAssemblies,
            name => ForbiddenAssemblyPrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void DomainAssemblyDoesNotReferenceBoundaryOrTimerTypes()
    {
        using var assemblyStream = File.OpenRead(typeof(PlaybackPresence).Assembly.Location);
        using var portableExecutable = new PEReader(assemblyStream);
        var metadata = portableExecutable.GetMetadataReader();
        var referencedTypes = metadata.TypeReferences
            .Select(metadata.GetTypeReference)
            .Select(type => string.Concat(
                metadata.GetString(type.Namespace),
                ".",
                metadata.GetString(type.Name)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            referencedTypes,
            typeName => ForbiddenAssemblyPrefixes.Any(
                prefix => typeName.StartsWith(prefix, StringComparison.Ordinal))
                || string.Equals(typeName, "System.Threading.Timer", StringComparison.Ordinal)
                || string.Equals(typeName, "System.Threading.PeriodicTimer", StringComparison.Ordinal)
                || typeName.StartsWith("Serilog.", StringComparison.Ordinal));
    }
}
