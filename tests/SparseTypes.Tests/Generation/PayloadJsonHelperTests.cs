using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

/// <summary>Covers the top-level ModelJson entry point.</summary>
/// <remarks>
/// No-STJ and partial-STJ omission ride on the same capability gate as the
/// nested resolver; see <c>StjOptionalGenerationTests</c> for that matrix.
/// </remarks>
public sealed class PayloadJsonHelperTests
{
    private const string NamespacedSource = """
        using SparseTypes;
        using System.Collections.Generic;

        namespace HelperProbe
        {
            [SparseModel]
            public partial class FirstWidget
            {
                public string? Label { get; set; }
            }

            [SparseModel]
            public partial class SecondWidget
            {
                public List<string> Tags { get; set; } = new();
            }

            [SparseModel]
            internal partial class InternalWidget
            {
                public int Count { get; set; }
            }
        }
        """;

    private const string CollisionSource = """
        using SparseTypes;

        namespace CollisionProbe
        {
            [SparseModel]
            public partial class CollisionWidget
            {
                public string? Label { get; set; }
            }

            public class CollisionWidgetJson
            {
            }
        }
        """;

    private static CSharpCompilation CreateCompilation(string source, bool includeStj)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Probe.cs");
        return CSharpCompilation.Create(
            "PayloadJsonHelperProbe",
            [tree],
            TrustedReferences(includeStj),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static List<MetadataReference> TrustedReferences(bool includeStj)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Where(path =>
                includeStj
                || !Path.GetFileName(path).StartsWith("System.Text.Json", StringComparison.Ordinal)
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        return references;
    }

    private static Dictionary<string, string> Run(CSharpCompilation compilation)
    {
        var (sources, diagnostics) = RunWithDiagnostics(compilation);
        diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return sources;
    }

    private static (
        Dictionary<string, string> Sources,
        System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics
    ) RunWithDiagnostics(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        var sources = result
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().SourceText.ToString(),
                StringComparer.Ordinal
            );
        return (sources, result.Diagnostics);
    }

    private static void CombinedCompilationHasNoErrors(
        CSharpCompilation compilation,
        Dictionary<string, string> sources
    )
    {
        var generated = sources.Select(pair =>
            CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)
        );
        var combined = compilation.AddSyntaxTrees(generated);
        combined
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void WithStj_EmitsDistinctHelpersForModelsSharingNamespace()
    {
        var sources = Run(CreateCompilation(NamespacedSource, includeStj: true));
        var merged = string.Concat(sources.Values);

        merged.ShouldContain("public static class FirstWidgetJson");
        merged.ShouldContain("public static class SecondWidgetJson");
        merged.ShouldContain("internal static class InternalWidgetJson");

        CombinedCompilationHasNoErrors(
            CreateCompilation(NamespacedSource, includeStj: true),
            sources
        );
    }

    [Test]
    public void HelperExposesDefaultTypeInfoAndResolver()
    {
        var sources = Run(CreateCompilation(NamespacedSource, includeStj: true));
        var merged = string.Concat(sources.Values);

        merged.ShouldContain(
            "global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<global::HelperProbe.FirstWidget.ChangePayload> Default"
        );
        merged.ShouldContain(
            "global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver Resolver"
        );
        // The helper reuses the nested resolver instead of a second codec.
        merged.ShouldContain("FirstWidget.PayloadJson.Default.ChangePayload");
        merged.ShouldContain("FirstWidget.PayloadJson.Default;");
    }

    [Test]
    public void WithoutStj_OmitsHelpers()
    {
        var sources = Run(CreateCompilation(NamespacedSource, includeStj: false));

        foreach (var pair in sources)
        {
            pair.Value.ShouldNotContain("System.Text.Json");
            pair.Value.ShouldNotContain("WidgetJson");
        }

        CombinedCompilationHasNoErrors(
            CreateCompilation(NamespacedSource, includeStj: false),
            sources
        );
    }

    [Test]
    public void UserDefinedHelperName_ReportsGeneratedNameCollision()
    {
        var (sources, diagnostics) = RunWithDiagnostics(
            CreateCompilation(CollisionSource, includeStj: true)
        );

        diagnostics
            .Where(static diagnostic => diagnostic.Id == "SPF009")
            .ShouldHaveSingleItem()
            .GetMessage()
            .ShouldContain("CollisionWidgetJson");
        // The failed model emits no surface, so no helper is generated.
        foreach (var pair in sources)
        {
            pair.Value.ShouldNotContain("partial class CollisionWidget");
        }
    }
}
