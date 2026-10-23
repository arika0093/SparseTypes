using System.Collections.Immutable;
using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits descriptor factories nested in per-model edit sessions.</summary>
/// <remarks>Reuses the existing descriptor emitters for the graph body.</remarks>
internal static class SparseDescriptorFactoryEmitter
{
    /// <summary>Appends the private descriptor factory nested in an edit session.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="runtimeNamespace">Runtime helper namespace.</param>
    public static void AppendNestedFactory(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        string runtimeNamespace
    )
    {
        var dialect = config.DescriptorDialect ?? throw new System.ArgumentException("A descriptor dialect is required.", nameof(config));
        using var innerBuilder = new SharedIndentedBuilder(code.CancellationToken);
        var observableType = ObservableParam(model, members, config, code.CancellationToken);
        SparseObservableDescriptorEmitter.Append(
            innerBuilder,
            model.ModelTypeName,
            members,
            runtimeNamespace,
            dialect,
            SparseDescriptorEmissionContext.ForFactory(observableType)
        );
        var body = innerBuilder.ToString();
        code.AppendLine("/// <summary>Descriptor factory bound to the owning observable.</summary>");
        code.AppendLine("private static class DescriptorFactory");
        code.OpenBlock();
        code.Append(body);
        code.CloseBlock();
    }

    /// <summary>Builds the nested factory text for focused emitter tests.</summary>
    public static string BuildFactorySource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        string runtimeNamespace
    )
    {
        using var code = new SharedIndentedBuilder(cancellationToken);
        AppendNestedFactory(code, model, members, config, runtimeNamespace);
        return code.ToString();
    }

    private static string ObservableParam(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var family = config.EffectiveFamilyNames;
        var simple = SparseObservableEmitter.ObservableTypeName(members, family);
        return SparseGeneratedPlacement.GetObservableReference(model, simple, config, cancellationToken);
    }
}
