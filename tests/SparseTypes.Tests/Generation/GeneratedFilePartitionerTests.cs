using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

public sealed class GeneratedFilePartitionerTests
{
    [Test]
    public void NamespaceScopedUsingsRemainWithGeneratedDeclarations()
    {
        var model = new SparseModelInfo("Root", "global::Root", string.Empty, true, false, false, "Root.g.cs", null);
        const string source = """
            namespace SparseTypes.Generated
            {
                using System;
                internal static class RootOperations { }
            }
            """;

        var output = SparseTypesGeneratedFilePartitioner.Partition(
            model,
            null,
            source,
            "SparseTypes.Generated",
            "SerializeObjects",
            CancellationToken.None
        );

        var generated = output.Single(static item => item.HintName.EndsWith(".Generated.g.cs", StringComparison.Ordinal)).Source;
        generated.ShouldContain("class RootOperations");
        generated.ShouldContain("using System;");
    }
}
