using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

// Verify descriptor factories can materialize member attributes without
// exposing generated bridges on model types.
public sealed class AttributeBridgeTests
{
    private const string AttributeExpression = "new global::System.ComponentModel.DescriptionAttribute(\"bridge-probe\")";

    private static SparseTypeModel TextType() =>
        new(
            "global::System.String?",
            "global::System.String",
            "global::System.String",
            IsReferenceType: true,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );

    private static SparseTypeModel IntType() =>
        new(
            "global::System.Int32",
            "global::System.Int32",
            "global::System.Int32",
            IsReferenceType: false,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );

    private static ImmutableArray<SparseMemberModel> AttributedMembers() =>
        ImmutableArray.Create(
            new SparseMemberModel(
                0,
                new SparsePropertyModel(
                    "Label",
                    TextType(),
                    JsonPropertyName: "Label",
                    HasExplicitJsonPropertyName: false,
                    JsonIgnoreCondition: 0,
                    IsNullable: true,
                    AttributeExpressions: AttributeExpression
                ),
                null,
                SparseMergeModes.Replace,
                SparseCollectionInfo.Unsupported,
                null,
                null,
                ChildIsStructural: false,
                ChildIsReferenceType: true
            ),
            new SparseMemberModel(
                1,
                new SparsePropertyModel(
                    "RetryCount",
                    IntType(),
                    JsonPropertyName: "RetryCount",
                    HasExplicitJsonPropertyName: false,
                    JsonIgnoreCondition: 0,
                    IsNullable: false
                ),
                null,
                SparseMergeModes.Replace,
                SparseCollectionInfo.Unsupported,
                null,
                null,
                ChildIsStructural: false,
                ChildIsReferenceType: true
            )
        );

    private static SparseModelInfo BridgeModel() =>
        new(
            "ReadModel",
            "global::Ns.ReadModel",
            "Ns",
            IsGlobalNamespace: false,
            IsStruct: false,
            IsRecord: false,
            HintName: "ReadModel.SparseTypes.g.cs",
            Constructor: null,
            IgnoredSettablePropertyNames: ImmutableArray<string>.Empty,
            IsPublic: true
        );

    private static SparseGeneratorConfig ProductConfigWithoutDescriptors()
    {
        var configuration = (SparseGeneratorConfig)(
            typeof(SparseTypesGenerator).GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );
        return configuration with { DescriptorDialect = null };
    }

    private static SparseGeneratorConfig ProductConfig() =>
        (SparseGeneratorConfig)(
            typeof(SparseTypesGenerator).GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );

    private static (string Surface, string? Implementation) BuildSplit(SparseGeneratorConfig config, ImmutableArray<SparseMemberModel> members) =>
        SparseFragmentEmitter.BuildSplitSource(
            BridgeModel(),
            members,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            bclHashSetImplementsReadOnlySet: false,
            bclHashSetSupportsCapacity: false,
            CancellationToken.None,
            config
        );

    private static string BuildSurface(SparseGeneratorConfig config, ImmutableArray<SparseMemberModel> members) =>
        BuildSplit(config, members).Surface;

    [Test]
    public void DescriptorFactoryOwnsAttributeExpressionsWithoutModelBridges()
    {
        var config = ProductConfigWithoutDescriptors();
        var members = AttributedMembers();
        var surface = BuildSurface(config, members);
        surface.ShouldNotContain("__SparseAttributes_");

        // The nested factory can access model-private attribute types directly.
        var factoryConfig = ProductConfig();
        var factory = SparseDescriptorFactoryEmitter.BuildFactorySource(
            BridgeModel(),
            members,
            factoryConfig,
            CancellationToken.None,
            factoryConfig.RuntimeDialect!.Namespace
        );
        factory.ShouldContain("new global::System.Attribute[]");
        factory.ShouldNotContain("__SparseAttributes_");
    }

    [Test]
    public void DescriptorFactoryEmitsItsSignatureReceiversAndAttributeExpressionsDirectly()
    {
        var config = ProductConfig();
        var source = SparseDescriptorFactoryEmitter.BuildFactorySource(
            BridgeModel(),
            AttributedMembers(),
            config,
            CancellationToken.None,
            config.RuntimeDialect!.Namespace
        );

        source.ShouldContain("Create(");
        source.ShouldContain("new global::System.Attribute[]");
        source.ShouldNotContain("__SparseAttributes_");
        source.ShouldContain("observable.Label");
        source.ShouldContain("observable.__model.Label");
        source.ShouldNotContain("__SparseGetDescriptors_");
        source.ShouldNotContain("this.");

        var customConfig = config with
        {
            DescriptorDialect = config.DescriptorDialect! with
            {
                DescriptorSetInterface = "global::Downstream.IDescriptorSet",
                PathType = "global::Downstream.Path",
            },
        };
        var customSource = SparseDescriptorFactoryEmitter.BuildFactorySource(
            BridgeModel(),
            AttributedMembers(),
            customConfig,
            CancellationToken.None,
            config.RuntimeDialect!.Namespace
        );
        customSource.ShouldContain("internal static global::Downstream.IDescriptorSet Create(");
        customSource.ShouldContain("global::Downstream.Path pathPrefix");
    }

    [Test]
    public void NoDescriptorDialect_OutputsCompileWithoutMissingBridges()
    {
        var config = ProductConfigWithoutDescriptors();
        var members = AttributedMembers();
        var (surface, splitImplementation) = BuildSplit(config, members);
        splitImplementation.ShouldNotBeNull();
        var implementations = SparseFragmentEmitter.BuildImplementationSources(
            BridgeModel(),
            members,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            CancellationToken.None,
            config
        );
        implementations.ShouldNotBeEmpty();

        surface.ShouldNotContain("__SparseAttributes_");
        implementations
            .Select(static source => source.Source)
            .ShouldAllBe(static text => !text.Contains("__SparseAttributes_", StringComparison.Ordinal));

        var cores = SparseEditSessionEmitter.RenderCoreSources(config.EditSessionDialect!, config.RuntimeDialect!, config.PatchDialect!.Value);
        var implementationNamespace = SparseGeneratedOnceEmitter.TryGetNamespace(config)!;
        var jsonCodecs = SparseGeneratedOnceEmitter.RenderJsonCodecs(
            implementationNamespace,
            config.PatchDialect!.Value,
            emitCodecs: true,
            CancellationToken.None
        );
        var jsonPayloadCodecs = SparseGeneratedOnceEmitter.RenderJsonPayloadCodecs(implementationNamespace, emitCodecs: true, CancellationToken.None);
        var observationCoordinator = SparseGeneratedOnceEmitter.RenderObservationCoordinator(implementationNamespace, CancellationToken.None);
        const string stub = """
            namespace Ns
            {
                public partial class ReadModel
                {
                    [System.ComponentModel.Description("bridge-probe")]
                    public string? Label { get; set; }
                    public int RetryCount { get; set; }
                }
            }
            """;
        var trees = implementations
            .Select(static source => CSharpSyntaxTree.ParseText(source.Source))
            .Prepend(CSharpSyntaxTree.ParseText(surface, path: "Surface.cs"))
            .Prepend(CSharpSyntaxTree.ParseText(splitImplementation!, path: "Implementation.cs"))
            .Append(CSharpSyntaxTree.ParseText(jsonCodecs, path: "JsonCodecs.cs"))
            .Append(CSharpSyntaxTree.ParseText(jsonPayloadCodecs, path: "JsonPayloadCodecs.cs"))
            .Append(CSharpSyntaxTree.ParseText(observationCoordinator, path: "ObservationCoordinator.cs"))
            .Append(CSharpSyntaxTree.ParseText(cores.Core, path: "EditSessionCore.cs"))
            .Append(CSharpSyntaxTree.ParseText(cores.CurrentCore, path: "EditSessionWithCurrentCore.cs"))
            .Append(CSharpSyntaxTree.ParseText(stub, path: "Models.cs"))
            .ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(static path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseTypes.Optional<>).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "NoDescriptorBridgeProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }
}
