using Microsoft.CodeAnalysis;

namespace SparseTypes.Tests;

public sealed class TestMetadataReferencesTests
{
    [Test]
    public void PlatformReferencesAreSharedAcrossParallelRequests()
    {
        var path = typeof(object).Assembly.Location;
        var references = new PortableExecutableReference[32];
        Parallel.For(0, references.Length, index => references[index] = TestMetadataReferences.FromFile(path));

        foreach (var reference in references)
        {
            ReferenceEquals(reference, references[0]).ShouldBeTrue();
        }

        var aliased = references[0].WithAliases(["Probe"]);
        aliased.Properties.Aliases.ShouldBe(["Probe"]);
        TestMetadataReferences.FromFile(path).Properties.Aliases.ShouldBeEmpty();
    }

    [Test]
    public void TemporaryProbeReferencesAreNotCached()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sparse-metadata-{Guid.NewGuid():N}.dll");
        try
        {
            File.Copy(typeof(object).Assembly.Location, path);
            var first = TestMetadataReferences.FromFile(path);
            var second = TestMetadataReferences.FromFile(path);

            ReferenceEquals(first, second).ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
