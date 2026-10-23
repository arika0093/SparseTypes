using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Shared context for payload JSON resolver emission.</summary>
internal sealed record PayloadJsonEmissionContext(
    System.Collections.Immutable.ImmutableArray<SparseMemberModel> Members,
    SparseFragmentPatchEmitter.SparsePatchDialect Dialect,
    string ModelType,
    string? ImplementationNamespace,
    bool EmitTopLevelCodecs,
    string Runtime,
    string ContainerAlias,
    string ConverterName,
    string ResolverName,
    System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel> Pocos,
    bool SupportsJson
)
{
    public SparsePayloadContract Contract { get; init; } = SparsePayloadContract.Create(Members);

    /// <summary>Creates the emission context for one model.</summary>
    public static PayloadJsonEmissionContext? TryCreate(
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace,
        bool emitTopLevelCodecs = false,
        System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel> pocos = default
    ) => TryCreate(SparsePayloadContract.Create(members), dialect, modelType, implementationNamespace, emitTopLevelCodecs, pocos);

    /// <summary>Creates the emission context from the canonical payload contract.</summary>
    public static PayloadJsonEmissionContext? TryCreate(
        SparsePayloadContract contract,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace,
        bool emitTopLevelCodecs = false,
        System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel> pocos = default
    )
    {
        if (modelType is null)
        {
            return null;
        }

        var supportsJson = dialect.EmitJson && dialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver;
        if (!supportsJson && dialect.PayloadSchemaBundleType is null)
        {
            return null;
        }

        var runtime = dialect.RuntimeNamespace;
        var containerAlias = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(dialect, modelType);
        return new PayloadJsonEmissionContext(
            contract.Members.Select(static member => member.Member).ToImmutableArray(),
            dialect,
            modelType,
            implementationNamespace,
            emitTopLevelCodecs,
            runtime,
            containerAlias,
            emitTopLevelCodecs ? containerAlias + "PayloadJsonConverter" : "PayloadJsonConverter",
            emitTopLevelCodecs ? containerAlias + "PayloadJsonResolver" : "PayloadJsonResolver",
            pocos.IsDefault ? System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel>.Empty : pocos,
            supportsJson
        )
        {
            Contract = contract,
        };
    }

    /// <summary>Qualified DTO type for the current model.</summary>
    public string Dto(string suffix) => ContainerReference(ModelType) + "." + SparseChangeSetPayloadEmitter.PayloadName(ModelType, suffix);

    /// <summary>Documented composition entry point for diagnostics.</summary>
    /// <remarks>Points custom-type failures at the model-facing JSON helper.</remarks>
    public string ResolverReference => ModelType + ".PayloadJson.Resolver";

    /// <summary>Qualified member DTO type for the current model.</summary>
    public string MemberDto(string suffix, int id) =>
        ContainerReference(ModelType) + "." + SparseChangeSetPayloadEmitter.PayloadMemberName(ModelType, suffix, id);

    /// <summary>Qualified child DTO type.</summary>
    public string ChildDto(string childModel, string suffix) =>
        SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(Dialect, childModel, suffix, ImplementationNamespace);

    /// <summary>Qualified converter implementation for a model's payload.</summary>
    public string ConverterReference(string modelType) => JsonCodecTypeReference(modelType, "PayloadJsonConverter");

    /// <summary>Qualified resolver implementation for the current model.</summary>
    public string ResolverTypeReference => JsonCodecTypeReference(ModelType, "PayloadJsonResolver");

    private string JsonCodecTypeReference(string modelType, string name)
    {
        if (!EmitTopLevelCodecs)
        {
            return ImplementationContainerReference(modelType) + "." + name;
        }

        var implementationNamespace = ImplementationNamespace;
        if (implementationNamespace is null)
        {
            var identity = modelType.StartsWith("global::", System.StringComparison.Ordinal) ? modelType.Substring("global::".Length) : modelType;
            var separator = identity.LastIndexOf('.');
            implementationNamespace = separator < 0 ? string.Empty : identity.Substring(0, separator);
        }

        var prefix = implementationNamespace.Length == 0 ? "global::" : "global::" + implementationNamespace + ".";
        var container = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(Dialect, modelType);
        return prefix + (name.StartsWith(container, System.StringComparison.Ordinal) ? name : container + name);
    }

    private string ContainerReference(string modelType) =>
        EmitTopLevelCodecs && ImplementationNamespace is null ? modelType + "." + ContainerAlias : ContainerAlias;

    private string ImplementationContainerReference(string modelType)
    {
        var container = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(Dialect, modelType);
        return string.IsNullOrEmpty(ImplementationNamespace)
            ? modelType + "." + container
            : SparseGeneratedPlacement.QualifyPayloadContainer(container, ImplementationNamespace);
    }
}
