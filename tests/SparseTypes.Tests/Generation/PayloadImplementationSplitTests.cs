using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

/// <summary>Source-placement regression tests for grouped model output.</summary>
public sealed class PayloadImplementationSplitTests
{
    private static Dictionary<string, string> Files() =>
        new(StringComparer.Ordinal)
        {
            ["SplitModels.cs"] = """
                using SparseTypes;
                using System.Collections.Generic;
                [SparseModel]
                public partial class SplitParent
                {
                    public string Label { get; set; } = "";
                    public SplitChild Child { get; set; } = new();
                    public List<string> Tags { get; set; } = new();
                }
                [SparseModel]
                public partial class SplitChild
                {
                    public int Count { get; set; }
                }
                """,
        };

    private static Dictionary<string, string> Sources()
    {
        var trees = Files().Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)).ToArray();
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SparsePayloadSplitProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new SparseTypesGenerator().AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var result = driver.GetRunResult();
        return result
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(static group => group.Key, static group => group.First().SourceText.ToString(), StringComparer.Ordinal);
    }

    private static string FileFor(Dictionary<string, string> sources, string modelName, string category) =>
        sources.Single(pair => pair.Key.Contains(modelName, StringComparison.Ordinal) && pair.Key.EndsWith("." + category + ".g.cs", StringComparison.Ordinal)).Value;

    private static string OperationContainer(string modelName) =>
        SparseGeneratedPlacement.GetImplementationContainer(
            new SparseModelInfo(modelName, "global::" + modelName, string.Empty, true, false, false, "unused", null),
            CancellationToken.None
        );

    [Test]
    public void ModelOutputUsesResponsibilityBasedFiles()
    {
        var sources = Sources();
        var types = FileFor(sources, "SplitParent", "Types");
        var extensions = FileFor(sources, "SplitParent", "Extensions");
        var serialize = FileFor(sources, "SplitParent", "Serialize");
        var json = FileFor(sources, "SplitParent", "Json");
        var generated = FileFor(sources, "SplitParent", "Generated");

        types.ShouldContain("public partial class SplitParent");
        types.ShouldContain("public sealed class ChangePayload");
        types.ShouldNotContain("static partial class SplitParent_");
        extensions.ShouldContain("static partial class SplitParentExtensions_");
        serialize.ShouldContain("static partial class SplitParent_");
        serialize.ShouldContain("public class Core");
        serialize.ShouldContain("public sealed class Root");
        serialize.ShouldContain("\n        public class Core\n        {");
        serialize.ShouldContain("\n            public sealed class MixedApplyResult\n            {");
        serialize.ShouldContain("\n        public abstract class Change { }");
        serialize.ShouldNotContain("\n            public class Core\n");
        serialize.ShouldNotContain("PayloadJsonConverter :");
        json.ShouldContain("PayloadJsonConverter :");
        json.ShouldContain("PayloadJsonResolver :");
        json.ShouldNotContain("static partial class SplitParent_");
        generated.ShouldContain("FragmentOperations");
        generated.ShouldContain("SplitParent_");
        generated.ShouldContain("FragmentBuilder");
        generated.ShouldContain("SparsePaths");
        generated.ShouldNotContain("namespace SparseTypes.Generated\n{\n\n");
        sources.Keys.ShouldAllBe(key => !key.Contains("global__", StringComparison.Ordinal));
        sources.Keys.ShouldAllBe(key => !key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal));
        sources.Keys.ShouldContain("Generated.g.cs");
    }

    [Test]
    public void ModelFilesOmitEmptyOperationContainersAndPreserveNestedPayloads()
    {
        var sources = Sources();
        var parentSerialize = FileFor(sources, "SplitParent", "Serialize");
        var parentJson = FileFor(sources, "SplitParent", "Json");
        var parentGenerated = FileFor(sources, "SplitParent", "Generated");
        var container = OperationContainer("SplitParent");

        parentSerialize.ShouldContain("global::SparseTypes.Generated.SplitChild_");
        parentJson.ShouldContain("public override global::SplitParent.ChangePayload? Read(");
        parentJson.ShouldContain("public override void Write(");
        parentJson.ShouldContain("\"members\"");
        parentJson.ShouldContain("\"kind\"");
        parentJson.ShouldNotContain("\"member\"");
        parentGenerated.ShouldNotContain("static partial class " + container + "\n");
        FileFor(sources, "SplitChild", "Serialize").ShouldContain("SplitChild_");
        FileFor(sources, "SplitChild", "Generated").ShouldNotContain("static partial class " + OperationContainer("SplitChild") + "\n");
    }
}
