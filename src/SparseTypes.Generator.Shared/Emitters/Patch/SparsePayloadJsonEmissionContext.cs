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
    string RootDiscriminator
)
{
    /// <summary>Creates the emission context for one model.</summary>
    public static PayloadJsonEmissionContext? TryCreate(
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace
    )
    {
        if (
            !dialect.EmitJson
            || !dialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver
            || modelType is null
        )
            return null;
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
            RootDiscriminatorFor(members)
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
            builder.Append('$');
        return builder.ToString();
    }

    /// <summary>Qualified DTO type for the current model.</summary>
    public string Dto(string suffix) =>
        ContainerAlias + "." + SparseChangeSetPayloadEmitter.PayloadName(ModelType, suffix);

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
