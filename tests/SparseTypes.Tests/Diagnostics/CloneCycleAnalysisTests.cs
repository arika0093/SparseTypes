using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Diagnostics;

public sealed class CloneCycleAnalysisTests
{
    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> Sources
    ) Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = tpa.Select(path =>
                (MetadataReference)MetadataReference.CreateFromFile(path)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "SparseCloneCycleProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (
            runResult.Diagnostics,
            runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray()
        );
    }

    private static string ChainSource(int depth)
    {
        // T0..T(d-1) each hold two references to the next level; Td is a
        // scalar leaf. The type graph has O(d) nodes and no cycles, but the
        // former per-path walk expanded it O(2^d) times.
        var builder = new StringBuilder();
        builder.AppendLine("using SparseTypes;");
        for (var level = 0; level < depth; level++)
        {
            builder.AppendLine("[SparseModel]");
            builder.AppendLine($"public partial class ChainNode{level:00}");
            builder.AppendLine("{");
            builder.AppendLine(
                $"    public ChainNode{(level + 1):00} Left {{ get; set; }} = null!;"
            );
            builder.AppendLine(
                $"    public ChainNode{(level + 1):00} Right {{ get; set; }} = null!;"
            );
            builder.AppendLine("}");
        }
        builder.AppendLine("[SparseModel]");
        builder.AppendLine($"public partial class ChainNode{depth:00}");
        builder.AppendLine("{");
        builder.AppendLine("    public int Value { get; set; }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void ShouldHaveNoSpfDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<GeneratedSourceResult> sources
    )
    {
        // SPF026 is an informational note for constructor-bound members;
        // only errors indicate a behavior change here.
        diagnostics
            .Where(d =>
                d.Id.StartsWith("SPF", StringComparison.Ordinal)
                && d.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
        sources
            .Any(s => s.HintName.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    [Arguments(4)]
    [Arguments(8)]
    [Arguments(12)]
    [Arguments(16)]
    [Timeout(120_000)]
    public void DeepSharedChain_CompletesWithoutDiagnostics(
        int depth,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (diagnostics, sources) = Run(ChainSource(depth));
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    [Timeout(120_000)]
    public void VeryDeepSharedChain_CompletesWithoutDiagnostics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The former walk needs ~2^20 expansions here; shared completed
        // nodes keep this linear.
        var (diagnostics, sources) = Run(ChainSource(20));
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    [Timeout(120_000)]
    public void WideDiamondWithRepeatedTopLevelProperties_CompletesWithoutDiagnostics(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new StringBuilder();
        builder.AppendLine("using SparseTypes;");
        builder.AppendLine("[SparseModel]");
        builder.AppendLine("public partial class DiamondRoot");
        builder.AppendLine("{");
        for (var index = 0; index < 8; index++)
            builder.AppendLine($"    public DiamondChild Child{index} {{ get; set; }} = new();");
        builder.AppendLine("}");
        builder.AppendLine("[SparseModel]");
        builder.AppendLine("public partial class DiamondChild");
        builder.AppendLine("{");
        for (var index = 0; index < 8; index++)
            builder.AppendLine($"    public DiamondLeaf Leaf{index} {{ get; set; }} = new();");
        builder.AppendLine("}");
        builder.AppendLine("[SparseModel]");
        builder.AppendLine("public partial class DiamondLeaf");
        builder.AppendLine("{");
        builder.AppendLine("    public int Value { get; set; }");
        builder.AppendLine("}");
        var (diagnostics, sources) = Run(builder.ToString());
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void RegisterableMutableCycle_GeneratesWithoutDiagnostics()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class CycleA
            {
                public CycleB? Peer { get; set; }
            }
            [SparseModel]
            public partial class CycleB
            {
                public CycleA? Peer { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void ReferenceSafeEdge_BreaksConstructorCycle()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class SafeCycleNode
            {
                public SafeCycleNode(SafeCycleNode next) { Next = next; }
                [SparseCloneReferenceSafe]
                public SafeCycleNode Next { get; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void CollectionMediatedConstructorCycle_ReportsCyclicProperty()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            [SparseModel]
            public partial class ListCycleNode
            {
                public ListCycleNode(List<ListCycleNode> next) { Next = next; }
                public List<ListCycleNode> Next { get; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        var cycleErrors = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        cycleErrors.Length.ShouldBe(1);
        cycleErrors[0].GetMessage().ShouldContain("Next");
        cycleErrors[0].Location.IsInSource.ShouldBeTrue();
        sources.ShouldBeEmpty();
    }

    [Test]
    public void StructMediatedUnsupportedMember_ReportsPayloadProperty()
    {
        const string source = """
            using SparseTypes;
            public struct StructCyclePayload
            {
                public StructCycleHolder? Holder { get; set; }
            }
            [SparseModel]
            public partial class StructCycleHolder
            {
                public StructCyclePayload Payload { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        var cloneErrors = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        cloneErrors.Length.ShouldBe(1);
        cloneErrors[0].GetMessage().ShouldContain("Payload");
        sources.ShouldBeEmpty();
    }

    private static ImmutableArray<Diagnostic> Spf008ForProperty(
        ImmutableArray<Diagnostic> diagnostics,
        string propertyName
    )
    {
        var matches = diagnostics
            .Where(d =>
                d.Id == "SPF008"
                && d.Severity == DiagnosticSeverity.Error
                && d.GetMessage().Contains(propertyName, StringComparison.Ordinal)
                && d.Location.IsInSource
                && d.Location.SourceTree!.GetText()
                    .ToString(d.Location.SourceSpan)
                    .Contains(propertyName, StringComparison.Ordinal)
            )
            .ToImmutableArray();
        return matches;
    }

    [Test]
    public void SharedUnsupportedDescendant_ReportsEveryAffectedProperty()
    {
        // Both root properties reach the same unsupported member; sharing
        // completed cycle results must not drop either attribution.
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class SharedBadChild
            {
                public object Value { get; set; } = new object();
            }
            [SparseModel]
            public partial class TwoRefsRoot
            {
                public SharedBadChild First { get; set; } = new();
                public SharedBadChild Second { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        Spf008ForProperty(diagnostics, "Value").Length.ShouldBe(1);
        Spf008ForProperty(diagnostics, "First").Length.ShouldBe(1);
        Spf008ForProperty(diagnostics, "Second").Length.ShouldBe(1);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void SharedConstructorCycle_ReportsEveryAffectedProperty()
    {
        // The shared cycle node completes true under the first property; the
        // second property must still report its own location.
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class SharedCycleNode
            {
                public SharedCycleNode(SharedCycleNode next) { Next = next; }
                public SharedCycleNode Next { get; }
            }
            [SparseModel]
            public partial class TwoCycleRefs
            {
                public SharedCycleNode First { get; set; } = null!;
                public SharedCycleNode Second { get; set; } = null!;
            }
            """;
        var (diagnostics, sources) = Run(source);
        Spf008ForProperty(diagnostics, "Next").Length.ShouldBe(1);
        Spf008ForProperty(diagnostics, "First").Length.ShouldBe(1);
        Spf008ForProperty(diagnostics, "Second").Length.ShouldBe(1);
        sources.ShouldBeEmpty();
    }
}
