using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

/// <summary>Derived declarations that hide or ignore a base property must not resurrect the base property.</summary>
public sealed class ShadowedPropertyDiscoveryTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            "SparseShadowProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Compilation Generate(string source)
    {
        var compilation = CreateCompilation(source);
        CSharpGeneratorDriver.Create(new SparseTypesGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    private static ImmutableArray<string> ReadablePropertyNames(string source)
    {
        var compilation = CreateCompilation(source);
        var model = compilation.GetTypeByMetadataName("Derived") ?? throw new InvalidOperationException("Derived model not found.");
        return SparseModelDiscovery
            .GetReadableProperties(model, Config(), CancellationToken.None)
            .Select(static property => property.Name)
            .ToImmutableArray();
    }

    private static SparseGeneratorConfig Config() =>
        new(
            ModelAttributeMetadataName: "SparseTypes.SparseModelAttribute",
            IgnoreAttributeMetadataName: "SparseTypes.SparseIgnoreAttribute",
            RedactBeforeAttributeMetadataName: "SparseTypes.SparseRedactBeforeAttribute",
            MergeAttributeMetadataName: "SparseTypes.SparseMergeAttribute",
            MergeStrategyBaseMetadataName: "SparseTypes.FragmentMergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "SparseTypes.SparseCloneReferenceSafeAttribute",
            KeyAttributeMetadataName: "SparseTypes.SparseKeyAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "SPF001",
                "SPF002",
                "SPF003",
                "SPF004",
                "SPF005",
                "SPF006",
                "SPF007",
                "SPF008",
                "SPF009",
                "SPF010",
                "SPF011",
                "SPF013",
                "SPF014",
                "SPF017",
                "SPF019",
                "SPF021",
                "SPF022",
                "SPF023"
            ),
            HintNameSuffix: ".g.cs",
            PromotedHintNameSuffix: ".promoted.g.cs",
            StructuralHostPrefix: "__Host_"
        );

    [Test]
    public void IgnoredOverrideDoesNotGenerateBaseProperty()
    {
        const string source = """
            using SparseTypes;
            public class Base
            {
                public virtual string? Secret { get; set; }
            }
            [SparseModel]
            public partial class Derived : Base
            {
                public int Id { get; set; }
                [SparseIgnore]
                public override string? Secret { get; set; }
            }
            """;
        var output = Generate(source);
        ReadablePropertyNames(source).ShouldBe(["Id"]);
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    [Test]
    public void IgnoredNewHidingDoesNotGenerateBaseProperty()
    {
        const string source = """
            using SparseTypes;
            public class Base
            {
                public string? Secret { get; set; }
            }
            [SparseModel]
            public partial class Derived : Base
            {
                public int Id { get; set; }
                [SparseIgnore]
                public new string? Secret { get; set; }
            }
            """;
        var output = Generate(source);
        ReadablePropertyNames(source).ShouldBe(["Id"]);
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    [Test]
    public void HiddenIncompatibleNonPublicDeclarationDoesNotGenerateBaseProperty()
    {
        const string source = """
            using SparseTypes;
            public class Base
            {
                public string? Hidden { get; set; }
                public string? Shared { get; set; }
                public string? FieldHidden { get; set; }
            }
            [SparseModel]
            public partial class Derived : Base
            {
                public int Id { get; set; }
                private new int Hidden { get; set; }
                public new static int Shared { get; set; }
                public new int FieldHidden = 0;
            }
            """;
        var output = Generate(source);
        ReadablePropertyNames(source).ShouldBe(["Id"]);
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }
}
