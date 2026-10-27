using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

// Pin the model-facing surface: state and builder types nest in the model,
// while paths and implementation details live in SparseTypes.Generated.
public sealed class ModelFacingSurfaceTests
{
    private const string ShapesSource = """
        using SparseTypes;
        namespace Surface.Probe
        {
            [SparseModel]
            public partial class SurfaceClass
            {
                public string Name { get; set; } = string.Empty;
                public SharedPoco? Child { get; set; }
            }
            [SparseModel]
            public partial record SurfaceRecord(string Name)
            {
                public string? Note { get; set; }
                public SharedPoco? Child { get; set; }
            }
            [SparseModel]
            public partial struct SurfaceStruct
            {
                public string Name { get; set; }
            }
            [SparseModel]
            internal partial class SurfaceInternal
            {
                public string Name { get; set; } = string.Empty;
            }
            public partial class SharedPoco
            {
                public string Label { get; set; } = string.Empty;
            }
            [SparseModel]
            public partial class SurfaceSecondRoot
            {
                public string Name { get; set; } = string.Empty;
                public SharedPoco? Child { get; set; }
            }
        }
        namespace Surface.Probe.First
        {
            using SparseTypes;
            [SparseModel]
            public partial class Dup
            {
                public string Name { get; set; } = string.Empty;
            }
        }
        namespace Surface.Probe.Second
        {
            using SparseTypes;
            [SparseModel]
            public partial class Dup
            {
                public string Name { get; set; } = string.Empty;
            }
        }
        """;

    private const string GlobalSource = """
        using SparseTypes;
        [SparseModel]
        public partial class GlobalSurfaceModel
        {
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static readonly string[] BannedNested =
    [
        "SparseObservable",
        "DescriptorFactory",
        "PatchOperations",
        "ChangeSetOperations",
        "SparsePaths",
    ];

    private static Compilation Generate(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(static path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "ModelFacingSurfaceProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated;
    }

    private static void AssertClean(Compilation compilation)
    {
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    private static INamedTypeSymbol RequireModel(Compilation compilation, string metadataName)
    {
        var model = compilation.GetTypeByMetadataName(metadataName);
        model.ShouldNotBeNull($"Expected model '{metadataName}'.");
        return model!;
    }

    private static void AssertIntendedSurface(Compilation compilation, INamedTypeSymbol model, bool expectSessionFamily)
    {
        var nested = model.GetTypeMembers().Select(static type => type.Name).ToArray();
        foreach (var banned in BannedNested)
        {
            nested.ShouldNotContain(banned);
        }

        nested.Where(static name => name.EndsWith("FragmentOperations", StringComparison.Ordinal)).ShouldBeEmpty();
        foreach (var internalHelper in new[] { "__SparseBuildTempMap", "__SparseCollectTempMap", "__SparseRetargetTemps" })
        {
            model.GetMembers(internalHelper).ShouldBeEmpty();
        }
        foreach (var expected in new[] { "Fragment", "FragmentBuilder", "Patch", "ChangeSet", "ChangePayload", "PayloadJson" })
        {
            nested.ShouldContain(expected);
        }
        // UI/editing types nest in the model; structs skip the session family.
        nested.ShouldContain("ReadOnlyView");
        if (expectSessionFamily)
        {
            foreach (var expected in new[] { "Observable", "EditSession" })
            {
                nested.ShouldContain(expected);
            }
        }
        else
        {
            foreach (var unexpected in new[] { "Observable", "EditSession" })
            {
                nested.ShouldNotContain(unexpected);
            }
        }
        var fragmentBuilder = model.GetTypeMembers("FragmentBuilder").ShouldHaveSingleItem();
        fragmentBuilder.DeclaredAccessibility.ShouldBe(model.DeclaredAccessibility);
        compilation
            .GetSymbolsWithName(static name => name == "FragmentBuilder")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol => symbol.ContainingNamespace.ToDisplayString().StartsWith("SparseTypes.Generated", StringComparison.Ordinal))
            .Where(static symbol => symbol.ContainingType is null)
            .ShouldBeEmpty();
        var sparsePathsTypes = compilation
            .GetSymbolsWithName(static name => name.EndsWith("SparsePaths", StringComparison.Ordinal))
            .OfType<INamedTypeSymbol>()
            .Where(static symbol => symbol.ContainingNamespace.ToDisplayString().StartsWith("SparseTypes.Generated", StringComparison.Ordinal))
            .Where(static symbol => symbol.ContainingType is null)
            .ToArray();
        sparsePathsTypes.ShouldNotBeEmpty();
        foreach (var pathsType in sparsePathsTypes)
        {
            var editorBrowsable = pathsType
                .GetAttributes()
                .Single(attribute => attribute.AttributeClass?.ToDisplayString() == "System.ComponentModel.EditorBrowsableAttribute");
            editorBrowsable.ConstructorArguments.Single().Value.ShouldBe((int)EditorBrowsableState.Never);
        }

        // Payload JSON remains available without adding a Fragment JSON converter.
        var fragment = model.GetTypeMembers("Fragment").ShouldHaveSingleItem();
        fragment.GetMembers("JsonConverter").ShouldBeEmpty();
        var generatedSource = string.Join("\n", compilation.SyntaxTrees.Skip(1).Select(static tree => tree.GetText().ToString()));
        generatedSource.ShouldNotContain("EditorBrowsableState.Always");
        generatedSource.ShouldContain("PayloadJsonConverter");
        generatedSource.ShouldNotContain("JsonConverter<Fragment>");
        generatedSource.ShouldContain("ObserveField(global::SparseTypes.SparsePath path)");
        generatedSource.ShouldContain("public sealed class EditSession :\n");

        if (expectSessionFamily)
        {
            // Same-short-name roots share the simple extension prefix, so
            // scope the lookup to the model's own namespace.
            var modelNamespace = model.ContainingNamespace.ToDisplayString();
            var extensions = compilation
                .GetSymbolsWithName(name => name.StartsWith(model.Name + "Extensions_", StringComparison.Ordinal))
                .OfType<INamedTypeSymbol>()
                .Where(static symbol => symbol.DeclaringSyntaxReferences.Length > 0)
                .Where(symbol => string.Equals(symbol.ContainingNamespace.ToDisplayString(), modelNamespace, StringComparison.Ordinal))
                .ShouldHaveSingleItem();
            foreach (var method in extensions.GetMembers("CreateEditSession").OfType<IMethodSymbol>())
            {
                // Sessions nest in the model; the return type stays
                // model-scoped.
                method.ReturnType.ToDisplayString().ShouldContain(model.Name + ".EditSession");
            }
        }
    }

    [Test]
    public void IntendedSurfaceHoldsAcrossClassRecordStructAndInternal()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.SurfaceClass"), expectSessionFamily: true);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.SurfaceRecord"), expectSessionFamily: true);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.SurfaceStruct"), expectSessionFamily: false);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.SurfaceInternal"), expectSessionFamily: true);
    }

    [Test]
    public void GeneratedModelMembersShareOnePartialDeclaration()
    {
        var compilation = Generate(ShapesSource);
        var generatedTrees = compilation.SyntaxTrees.Skip(1).ToArray();
        var declarations = generatedTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Where(static declaration =>
                declaration.Identifier.ValueText is "SurfaceClass" or "SurfaceRecord" or "SurfaceStruct" or "SurfaceInternal" or "SurfaceSecondRoot"
            )
            .ToArray();

        foreach (var modelName in new[] { "SurfaceClass", "SurfaceRecord", "SurfaceStruct", "SurfaceInternal", "SurfaceSecondRoot" })
        {
            declarations.Count(declaration => declaration.Identifier.ValueText == modelName).ShouldBe(1);
        }
    }

    [Test]
    public void GeneratedNestedTypesUseTheModelIndentation()
    {
        var compilation = Generate(ShapesSource);
        var model = RequireModel(compilation, "Surface.Probe.SurfaceClass");
        foreach (var name in new[] { "Fragment", "Patch", "ChangeSet", "ChangePayload", "PayloadJson" })
        {
            var nested = model.GetTypeMembers(name).ShouldHaveSingleItem();
            var declaration = nested.DeclaringSyntaxReferences.ShouldHaveSingleItem().GetSyntax();
            declaration.GetLocation().GetLineSpan().StartLinePosition.Character.ShouldBe(8);
        }
    }

    [Test]
    public void GlobalNamespaceModelKeepsTheSameSurface()
    {
        var compilation = Generate(GlobalSource);
        AssertClean(compilation);
        AssertIntendedSurface(compilation, RequireModel(compilation, "GlobalSurfaceModel"), expectSessionFamily: true);
    }

    [Test]
    public void SameShortNameModelsGetDistinctStableContainers()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.First.Dup"), expectSessionFamily: true);
        AssertIntendedSurface(compilation, RequireModel(compilation, "Surface.Probe.Second.Dup"), expectSessionFamily: true);
        var sessions = compilation
            .GetSymbolsWithName("EditSession")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol => symbol.ContainingType is { Name: "Dup" })
            .Select(static symbol => symbol.ToDisplayString())
            .Distinct()
            .ToArray();
        // Both same-short-name roots nest their own session in the model.
        sessions.Length.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public void EditSessionMemberNameIsFreeOnTheModel()
    {
        const string source = """
            using SparseTypes;
            namespace Surface.Probe;
            [SparseModel]
            public partial class FreedSessionMemberModel
            {
                public string EditSession { get; set; } = string.Empty;
                public string Name { get; set; } = string.Empty;
            }
            """;
        var compilation = Generate(source);
        AssertClean(compilation);
        var model = RequireModel(compilation, "Surface.Probe.FreedSessionMemberModel");
        // A same-named property pushes the generated session behind a Sparse
        // prefix, mirroring observable/view collision resolution.
        model.GetTypeMembers("EditSession").ShouldBeEmpty();
        model.GetTypeMembers("SparseEditSession").ShouldHaveSingleItem();
    }

    [Test]
    public void EditSessionNestedTypeCollidesWithGenerated()
    {
        const string source = """
            using SparseTypes;
            namespace Surface.Probe;
            [SparseModel]
            public partial class CollidingSessionNestedModel
            {
                public string Name { get; set; } = string.Empty;
                public sealed class EditSession
                {
                    public string Note { get; set; } = string.Empty;
                }
            }
            """;
        var compilation = Generate(source);
        // The generated session nests in the model, so a user-nested
        // EditSession is a duplicate definition.
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldNotBeEmpty();
    }

    [Test]
    public void PromotedSharedPocoCarriesNoImplementationTypes()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        var poco = RequireModel(compilation, "Surface.Probe.SharedPoco");
        var nested = poco.GetTypeMembers().Select(static type => type.Name).ToArray();
        foreach (var banned in BannedNested)
        {
            nested.ShouldNotContain(banned);
        }
    }
}
