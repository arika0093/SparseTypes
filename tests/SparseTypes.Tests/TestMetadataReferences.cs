using System.Collections.Frozen;
using Microsoft.CodeAnalysis;

namespace SparseTypes.Tests;

internal static class TestMetadataReferences
{
    private static readonly FrozenDictionary<string, Lazy<PortableExecutableReference>> PlatformReferences = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Distinct(StringComparer.Ordinal)
        .ToFrozenDictionary(
            static path => path,
            static path => new Lazy<PortableExecutableReference>(() => MetadataReference.CreateFromFile(path)),
            StringComparer.Ordinal
        );

    // Platform assemblies stay fixed for the process; temporary probe DLLs may be rebuilt.
    internal static PortableExecutableReference FromFile(string path) =>
        PlatformReferences.TryGetValue(path, out var reference) ? reference.Value : MetadataReference.CreateFromFile(path);
}
