using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.CodeFixes;

namespace SparseTypes.Tests.CodeFixes;

/// <summary>Applies each code fix and re-runs the generator to prove the diagnostic clears.</summary>
public sealed class SparseTypesCodeFixTests
{
    private static AdhocWorkspace CreateWorkspace(string source, out Document document)
    {
        var workspace = new AdhocWorkspace();
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var project = workspace
            .CurrentSolution.AddProject("FixProbe", "FixProbe", LanguageNames.CSharp)
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
            )
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .WithMetadataReferences(references.Distinct());
        document = project.AddDocument("Probe.cs", source);
        return workspace;
    }

    private static async Task<string> ApplyFirstFixAsync(string source, string expectedId)
    {
        using var workspace = CreateWorkspace(source, out var document);
        var compilation = (await document.Project.GetCompilationAsync())!;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics;
        var diagnostic = diagnostics.FirstOrDefault(static candidate => true);
        diagnostics.Where(static candidate => true).ShouldNotBeEmpty();
        var target = diagnostics.First(d => d.Id == expectedId);
        target.Location.IsInSource.ShouldBeTrue($"expected {expectedId} to have a source location");

        var provider = new SparseTypesCodeFixProvider();
        provider.FixableDiagnosticIds.ShouldContain(expectedId);
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, target, (action, _) => actions.Add(action), CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context);
        actions.ShouldNotBeEmpty($"expected a code fix for {expectedId}");
        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var updated = solution.GetDocument(document.Id)!;
        return (await updated.GetTextAsync()).ToString();
    }

    private static async Task AssertFixedAsync(string source, string expectedId)
    {
        var fixedSource = await ApplyFirstFixAsync(source, expectedId);
        var tree = CSharpSyntaxTree.ParseText(fixedSource);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "FixVerify",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Where(d => d.Id == expectedId).ShouldBeEmpty($"fixed source still reports {expectedId}:\n{fixedSource}");
    }

    [Test]
    public async Task Spf001_AddPartial_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public class NonPartialModel
            {
                public string? Label { get; set; }
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF001");
        fixedSource.ShouldContain("partial");
        await AssertFixedAsync(source, "SPF001");
    }

    [Test]
    public async Task Spf002_RemoveAbstract_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public abstract partial class AbstractModel
            {
                public string? Label { get; set; }
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF002");
        fixedSource.ShouldNotContain("abstract partial");
        await AssertFixedAsync(source, "SPF002");
    }

    [Test]
    public async Task Spf003_AddConstructor_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class NoUsableCtor
            {
                public NoUsableCtor(string missing) { }
                public string Label { get; set; } = "";
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF003");
        fixedSource.ShouldContain("NoUsableCtor()");
        await AssertFixedAsync(source, "SPF003");
    }

    [Test]
    public async Task Spf005_RemoveMerge_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class AppendScalarModel
            {
                [SparseMerge(MergeMode.Append)]
                public string Name { get; set; } = "";
            }
            """;
        await AssertFixedAsync(source, "SPF005");
    }

    [Test]
    public async Task Spf006_RemoveRequired_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class RequiredPrivateSetterModel
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                public RequiredPrivateSetterModel() { }
                public required string Label { get; private set; } = "";
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF006");
        fixedSource.ShouldNotContain("required string");
        await AssertFixedAsync(source, "SPF006");
    }

    [Test]
    public async Task Spf007_AddReplace_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            public sealed class PlainPoco
            {
                public string? Name { get; set; }
            }
            [SparseModel]
            public partial class StructuralHolder
            {
                public PlainPoco Child { get; set; } = new();
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF007");
        fixedSource.ShouldContain("MergeMode.Replace");
        await AssertFixedAsync(source, "SPF007");
    }

    [Test]
    public async Task Spf008_MarkReferenceSafe_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class ObjectCloneModel
            {
                public object Value { get; set; } = new object();
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF008");
        fixedSource.ShouldContain("SparseCloneReferenceSafe");
        await AssertFixedAsync(source, "SPF008");
    }

    [Test]
    public async Task Spf011_AddReplace_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            public partial class UnkeyedItem
            {
                public string Name { get; set; } = "";
            }
            [SparseModel]
            public partial class UnkeyedHolder
            {
                public List<UnkeyedItem> Items { get; set; } = new();
            }
            """;
        await AssertFixedAsync(source, "SPF011");
    }

    [Test]
    public async Task Spf014_RemoveTypeLevelKey_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            [SparseKey]
            public partial class BadKeyItem
            {
                public string Id { get; set; } = "";
            }
            [SparseModel]
            public partial class BadKeyHolder
            {
                public List<BadKeyItem> Items { get; set; } = new();
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF014");
        fixedSource.ShouldNotContain("[SparseKey]");
        await AssertFixedAsync(source, "SPF014");
    }

    [Test]
    public async Task Spf017_MakeKeyPublic_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            public partial class PrivateKeyItem
            {
                [SparseKey]
                private string Id { get; set; } = "";
            }
            [SparseModel]
            public partial class PrivateKeyHolder
            {
                public List<PrivateKeyItem> Items { get; set; } = new();
            }
            """;
        await AssertFixedAsync(source, "SPF017");
    }

    [Test]
    public async Task Spf022_RemoveIgnore_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            public partial class IgnoredKeyItem
            {
                [SparseKey, SparseIgnore]
                public string Id { get; set; } = "";
            }
            [SparseModel]
            public partial class IgnoredKeyHolder
            {
                public List<IgnoredKeyItem> Items { get; set; } = new();
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF022");
        fixedSource.ShouldNotContain("SparseIgnore");
        await AssertFixedAsync(source, "SPF022");
    }

    [Test]
    public async Task Spf023_RemoveIgnore_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class IgnoredInitModel
            {
                [SparseIgnore]
                public string Hidden { get; init; } = "";
            }
            """;
        await AssertFixedAsync(source, "SPF023");
    }

    [Test]
    public async Task Spf024_RemoveUnassigned_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System.Collections.Generic;
            public partial class SentinelItem
            {
                [SparseKey(Unassigned = null)]
                public int Id { get; set; }
            }
            [SparseModel]
            public partial class SentinelHolder
            {
                public List<SentinelItem> Items { get; set; } = new();
            }
            """;
        await AssertFixedAsync(source, "SPF024");
    }

    [Test]
    public async Task Spf032_RemoveInitializer_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System;
            using System.Collections.Generic;
            public partial class TempItem
            {
                [SparseKey]
                public int? Id { get; set; }
                [SparseTemporaryKey]
                public Guid? TempId { get; set; } = null;
            }
            [SparseModel]
            public partial class TempHolder
            {
                public List<TempItem> Items { get; set; } = new();
            }
            """;
        var fixedSource = await ApplyFirstFixAsync(source, "SPF032");
        fixedSource.ShouldNotContain("= null;");
        // SPF032 is a warning; generation continues, so the fix only needs to
        // clear the warning itself.
        var tree = CSharpSyntaxTree.ParseText(fixedSource);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "FixVerifyTemp",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Where(static d => d.Id == "SPF032").ShouldBeEmpty();
    }

    [Test]
    public async Task Spf031_RemoveTempArguments_ClearsDiagnostic()
    {
        const string source = """
            using SparseTypes;
            using System;
            using System.Collections.Generic;
            public partial class TempArgItem
            {
                [SparseKey]
                public int? Key { get; set; }
                [SparseTemporaryKey("arg")]
                public Guid? TempId { get; set; }
            }
            [SparseModel]
            public partial class TempArgHolder
            {
                public List<TempArgItem> Items { get; set; } = new();
            }
            """;
        await AssertFixedAsync(source, "SPF031");
    }

    [Test]
    public void Provider_CoversDocumentedFixableIds()
    {
        var provider = new SparseTypesCodeFixProvider();
        provider.GetFixAllProvider().ShouldNotBeNull();
        ImmutableArray<string> fixable = provider.FixableDiagnosticIds;
        fixable.ShouldContain("SPF001");
        fixable.ShouldContain("SPF032");
        // Deliberately unfixed: collisions and downstream plans need human judgment.
        fixable.ShouldNotContain("SPF009");
        fixable.ShouldNotContain("SPF010");
        fixable.ShouldNotContain("SPF028");
    }
}
