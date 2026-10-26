using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Diagnostics;

public sealed class ConstructorBindingTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Sources) Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SparseConstructorBindingProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (runResult.Diagnostics, runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray());
    }

    private static void ShouldHaveNoSpfDiagnostics(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<GeneratedSourceResult> sources)
    {
        // SPF026 is an informational note for constructor-bound members;
        // only errors indicate a behavior change here.
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        sources.Any(s => s.HintName.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Test]
    public void CaseInsensitiveParameterBinding_BindsGetterOnlyProperty()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class CaseBound
            {
                public CaseBound(string label) { Label = label; }
                public string Label { get; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void CaseVariantSameType_StaysAmbiguous()
    {
        // Two candidates share a case-insensitive name and the parameter
        // type, so neither constructor binds and SPF003 reports the model.
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class AmbiguousCase
            {
                public AmbiguousCase(int value) { Value = value; }
                public int Value { get; } = 0;
                public int value { get; } = 0;
            }
            """;
        var (diagnostics, sources) = Run(source);
        var missing = diagnostics.Where(d => d.Id == "SPF003").ToImmutableArray();
        missing.Length.ShouldBe(1);
        missing[0].GetMessage().ShouldContain("AmbiguousCase");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void CaseVariantDifferentType_DisambiguatesByParameterType()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class CaseSplitTypes
            {
                public CaseSplitTypes(string item) { this.item = item; }
                public int Item { get; } = 0;
                public string item { get; } = "";
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void HiddenProperty_BindsDerivedDeclaration()
    {
        const string source = """
            using SparseTypes;
            public class BindingBase
            {
                public string Name { get; } = "base";
            }
            [SparseModel]
            public partial class HiddenBinding : BindingBase
            {
                public HiddenBinding(string name) { Name = name; }
                public new string Name { get; } = "derived";
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }

    [Test]
    public void RefParameter_RejectsConstructor()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class RefBound
            {
                public RefBound(ref int value) { Value = value; }
                public int Value { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        var missing = diagnostics.Where(d => d.Id == "SPF003").ToImmutableArray();
        missing.Length.ShouldBe(1);
        missing[0].GetMessage().ShouldContain("RefBound");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void FirstFailingCandidate_FallsThroughToNextConstructor()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class MultiCandidate
            {
                public MultiCandidate(string missing) { Label = ""; Count = 0; }
                public MultiCandidate(string label, int count) { Label = label; Count = count; }
                public string Label { get; }
                public int Count { get; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        ShouldHaveNoSpfDiagnostics(diagnostics, sources);
    }
}
