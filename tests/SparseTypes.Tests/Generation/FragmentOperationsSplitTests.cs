using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

/// <summary>Source-placement regression tests for Fragment operations.</summary>
public sealed class FragmentOperationsSplitTests
{
    private static Dictionary<string, string> Files() =>
        new(StringComparer.Ordinal)
        {
            ["OpsModels.cs"] = """
                using SparseTypes;
                using System.Collections.Generic;
                [SparseModel]
                public partial class OpsParent
                {
                    public string Label { get; set; } = "";
                    public OpsChild Child { get; set; } = new();
                    [SparseMerge(MergeMode.Replace)]
                    public OpsPoco Meta { get; set; } = new();
                    public List<int> Scores { get; set; } = new();
                }
                [SparseModel]
                public partial class OpsChild
                {
                    public int Count { get; set; }
                }
                public sealed class OpsPoco
                {
                    public string Note { get; set; } = "";
                    public List<string> Notes { get; set; } = new();
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
            "SparseFragmentOperationsSplitProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new SparseTypesGenerator().AsSourceGenerator() });
        var result = driver.RunGenerators(compilation).GetRunResult();
        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return result
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(static group => group.Key, static group => group.First().SourceText.ToString(), StringComparer.Ordinal);
    }

    private static string ModelSource(Dictionary<string, string> sources, string modelName) =>
        string.Concat(sources.Where(pair => pair.Key.Contains(modelName, StringComparison.Ordinal) && pair.Key.EndsWith(".g.cs", StringComparison.Ordinal)).Select(static pair => pair.Value));

    [Test]
    public void CategorizedFilesKeepFacadesAndBridgesWithAlgorithms()
    {
        var sources = Sources();
        var merged = ModelSource(sources, "OpsParent");
        sources.Keys.ShouldNotContain(static key => key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal));

        // Facades delegate to the relocated operations class.
        merged.ShouldContain("FragmentOperations.From(value)");
        merged.ShouldContain("FragmentOperations.ToModel(this");
        merged.ShouldContain("FragmentOperations.Merge(this, higherPriority)");
        merged.ShouldContain("FragmentOperations.ApplyChanges(this, changes)");
        merged.ShouldContain("FragmentOperations.Diff(before, after)");
        merged.ShouldContain("FragmentOperations.DeepClone(this)");
        // Ergonomic state and builders stay model-facing.
        merged.ShouldContain("public sealed class Fragment");
        merged.ShouldContain("public sealed class ");
        merged.ShouldContain("FragmentBuilder");
        merged.ShouldContain("public static Fragment Empty");
        // Narrow CLR-mandated bridges stay: init-only storage with cycle
        // registration, the writable writer, and per-member equality shims.
        merged.ShouldContain("private Fragment(Fragment source,");
        merged.ShouldContain("__SparseWriteWritableTo");
        merged.ShouldContain("__SparseAreEqual");
        // Heavy algorithms stay in the model's Generated file.
        merged.ShouldContain("__SparseDiffCore");
        merged.ShouldContain("__Diff_");
        merged.ShouldContain("public static Fragment Merge(Fragment self,");
        merged.ShouldContain("public static Fragment ApplyChanges(Fragment self,");
    }

    [Test]
    public void GeneratedFileCarriesOperationsAndPocoHelpers()
    {
        var sources = Sources();
        var merged = ModelSource(sources, "OpsParent");

        // Same-file-only helpers narrow to file-scoped types on C# 11+.
        merged.ShouldContain(" static class ");
        merged.ShouldContain("FragmentOperations");
        merged.ShouldContain("public static Fragment From(");
        merged.ShouldContain("public static Fragment Merge(Fragment self,");
        merged.ShouldContain("public static Fragment ApplyChanges(Fragment self,");
        merged.ShouldContain("public static Fragment Diff(");
        merged.ShouldContain("__SparseDiffCore");
        merged.ShouldContain("__Diff_");
        merged.ShouldContain("ToModel(Fragment fragment");
        merged.ShouldContain(".Add(value, clone)");
        // POCO clone specialization moved out of the annotated model.
        merged.ShouldContain("__Clone_");
        // Nested models recurse through their own facades.
        merged.ShouldContain(".ToModel()");
    }

    [Test]
    public void SplitOperationsAreAssemblyVisibleForCrossModelCloning()
    {
        var sources = Sources();
        sources.Keys.Count(key => key.Contains("OpsParent", StringComparison.Ordinal) && key.EndsWith(".Types.g.cs", StringComparison.Ordinal)).ShouldBe(1);
        // Child model clone operations are referenced across generated files.
        ModelSource(sources, "OpsParent").ShouldContain("internal static class ");
    }

    [Test]
    public void LeafModelsOmitCycleTrackingWhileNestedModelsRetainIt()
    {
        var sources = Sources();
        var child = sources
            .Where(pair => pair.Key.Contains("OpsChild", StringComparison.Ordinal) && pair.Key.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .Aggregate(string.Concat);
        child.ShouldNotContain("CreateFromCycleContext");
        child.ShouldNotContain("CreateDiffCycleContext");
        child.ShouldNotContain("Cyclic reference detected during From");
        child.ShouldNotContain("Cyclic reference detected during Diff");
        child.ShouldContain("Dictionary<object, object> __sparse_clone_context)");
        child.ShouldNotContain("HashSet<object> __sparse_from_context");
        child.ShouldNotContain("HashSet<global::System.Collections.Generic.KeyValuePair<object, object>> __sparse_diff_context");
        child.ShouldNotContain("__sparse_from_path");
        child.ShouldNotContain("__sparse_diff_path");

        var parent = sources
            .Where(pair => pair.Key.Contains("OpsParent", StringComparison.Ordinal) && pair.Key.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .Aggregate(string.Concat);
        parent.ShouldContain("CreateFromCycleContext");
        parent.ShouldContain("CreateDiffCycleContext");
        parent.ShouldContain("Cyclic reference detected during From");
        parent.ShouldContain("Cyclic reference detected during Diff");
        parent.ShouldContain(".From(value.Child, __sparse_clone_context)");
        parent.ShouldContain("__Diff_0(before.Child, after.Child)");
        parent.ShouldNotContain(".From(value.Child, __sparse_clone_context, __sparse_from_context,");
        parent.ShouldNotContain("__Diff_0(before.Child, after.Child, __sparse_diff_context,");
    }

    [Test]
    public void CloneCopiesNonNullableImmutableMembersWithoutNullConditionals()
    {
        var sources = Sources();
        var child = sources
            .Where(pair => pair.Key.Contains("OpsChild", StringComparison.Ordinal) && pair.Key.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .Aggregate(string.Concat);

        child.ShouldContain("Count = source.Count");
        child.ShouldNotContain("source.Count is null");
    }
}
