namespace SparseTypes.Generator.Shared;

/// <summary>Shared context for payload JSON resolver emission.</summary>
internal sealed record PayloadJsonEmissionContext(
    System.Collections.Immutable.ImmutableArray<SparseMemberModel> Members,
    SparseFragmentPatchEmitter.SparsePatchDialect Dialect,
    string ModelType,
    string? ImplementationNamespace,
    string Runtime,
    string ContainerAlias,
    string ConverterName,
    string ResolverName,
    string RootDiscriminator,
    System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel> Pocos
)
{
    /// <summary>Creates the emission context for one model.</summary>
    public static PayloadJsonEmissionContext? TryCreate(
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace,
        System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel> pocos = default
    )
    {
        if (
            !dialect.EmitJson
            || !dialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver
            || modelType is null
        )
        {
            return null;
        }

        var runtime = dialect.RuntimeNamespace;
        var containerAlias = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(
            dialect,
            modelType
        );
        return new PayloadJsonEmissionContext(
            members,
            dialect,
            modelType,
            implementationNamespace,
            runtime,
            containerAlias,
            "PayloadJsonConverter",
            "PayloadJson",
            RootDiscriminatorFor(members),
            pocos.IsDefault
                ? System.Collections.Immutable.ImmutableArray<SparsePayloadJsonPocoModel>.Empty
                : pocos
        );
    }

    private static string RootDiscriminatorFor(
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        var builder = new System.Text.StringBuilder("$root");
        while (
            members.Any(member =>
                string.Equals(
                    member.Property.Name,
                    builder.ToString(),
                    System.StringComparison.Ordinal
                )
            )
        )
        {
            builder.Append('$');
        }

        return builder.ToString();
    }

    /// <summary>Qualified DTO type for the current model.</summary>
    public string Dto(string suffix) =>
        ContainerAlias + "." + SparseChangeSetPayloadEmitter.PayloadName(ModelType, suffix);

    /// <summary>Documented composition entry point for diagnostics.</summary>
    /// <remarks>Points custom-type failures at the top-level ModelJson helper instead of the nested resolver instance it delegates to.</remarks>
    public string ResolverReference
    {
        get
        {
            var dot = ModelType.LastIndexOf('.');
            var simple = dot >= 0 ? ModelType.Substring(dot + 1) : ModelType;
            return simple + "Json.Resolver";
        }
    }

    /// <summary>Qualified member DTO type for the current model.</summary>
    public string MemberDto(string suffix, int id) =>
        ContainerAlias
        + "."
        + SparseChangeSetPayloadEmitter.PayloadMemberName(ModelType, suffix, id);

    /// <summary>Qualified child DTO type.</summary>
    public string ChildDto(string childModel, string suffix) =>
        SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
            Dialect,
            childModel,
            suffix,
            ImplementationNamespace
        );
}
