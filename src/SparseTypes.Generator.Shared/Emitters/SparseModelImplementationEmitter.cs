using System.Collections.Immutable;
using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds model-nested UI/editing implementation sources.</summary>
/// <remarks>UI/editing types (<c>Observable</c>.</remarks>
internal static class SparseModelImplementationEmitter
{
    /// <summary>Builds the model-nested observable implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="runtimeNamespace">Runtime helper namespace.</param>
    /// <param name="descriptorDialect">Descriptor contracts, or null when disabled.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildObservableSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        string runtimeNamespace,
        SparseDescriptorDialect? descriptorDialect
    )
    {
        using var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseObservableEmitter.AppendObservable(innerBuilder, model.ModelTypeName, members, runtimeNamespace, descriptorDialect);
        var inner = innerBuilder.ToString();
        if (descriptorDialect is null && config.DescriptorDialect is { } configured && config.EffectiveEmissionFeatures.EmitObservable)
        {
            // Thin bridge keeps child "$proxy.__SparseGet_X(path)" call sites
            // working after the graph moves to DescriptorFactory.
            var accessor = SparseObservableDescriptorEmitter.AccessorName(model.ModelTypeName);
            var factoryRef = SparseGeneratedPlacement.GetDescriptorFactoryReference(model, config, cancellationToken);
            var bridge =
                "    internal "
                + configured.DescriptorSetInterface
                + " "
                + accessor
                + "("
                + configured.EffectivePathType(runtimeNamespace)
                + " pathPrefix) => "
                + factoryRef
                + ".Create(this, pathPrefix);\n";
            var privateMarker = "\n    private ";
            var privateIndex = inner.IndexOf(privateMarker, System.StringComparison.Ordinal);
            if (privateIndex >= 0)
            {
                inner = inner.Substring(0, privateIndex + 1) + bridge + inner.Substring(privateIndex + 1);
            }
            else
            {
                var closing = "\n}\n";
                var index = inner.LastIndexOf(closing, System.StringComparison.Ordinal);
                if (index >= 0)
                {
                    inner = inner.Substring(0, index) + "\n" + bridge + inner.Substring(index + 1);
                }
            }
        }

        var source = WrapInModel(model, inner, cancellationToken);
        return new SparseGeneratedSource(SparseGeneratedPlacement.ObservableHintName(model, cancellationToken), source);
    }

    /// <summary>Builds the model-nested read-only-view implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="readOnlyViewModels">POCO view models.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="implementationNamespace">Explicit namespace for shared adapters.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildReadOnlyViewSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        string? implementationNamespace
    )
    {
        using var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseReadOnlyViewEmitter.AppendReadOnlyView(innerBuilder, model.ModelTypeName, members, readOnlyViewModels, implementationNamespace);
        _ = readOnlyViewModels;
        var inner = innerBuilder.ToString();
        var source = WrapInModel(model, inner, cancellationToken);
        return new SparseGeneratedSource(SparseGeneratedPlacement.ReadOnlyViewHintName(model, cancellationToken), source);
    }

    /// <summary>Builds the model-nested per-model edit-session implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildEditSessionSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var family = config.EffectiveFamilyNames;
        var observableSimple = SparseObservableEmitter.ObservableTypeName(members, family);
        var readOnlySimple = SparseReadOnlyViewEmitter.ReadOnlyViewTypeName(members, family);
        var observableRef = SparseGeneratedPlacement.GetObservableReference(model, observableSimple, config, cancellationToken);
        var readOnlyRef = SparseGeneratedPlacement.GetReadOnlyViewReference(model, readOnlySimple, config, cancellationToken);
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseRelocatedEditSessionCore.Append(code, model, members, config, observableRef, readOnlyRef);
        var source = WrapInModel(model, code.ToString(), cancellationToken);
        return new SparseGeneratedSource(SparseGeneratedPlacement.EditSessionHintName(model, cancellationToken), source);
    }

    private static string WrapInModel(SparseModelInfo model, string inner, CancellationToken cancellationToken)
    {
        // Mirrors the surface declaration keyword so the nested UI types land
        // in the annotated model partial (records/structs included).
        var accessibility = model.IsPublic ? "public " : "internal ";
        string keyword;
        if (model.IsStruct)
        {
            keyword = accessibility + (model.IsRecord ? "partial record struct " : "partial struct ");
        }
        else
        {
            keyword = accessibility + (model.IsRecord ? "partial record " : "partial class ");
        }

        using var code = new SharedIndentedBuilder(cancellationToken);
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        if (!model.IsGlobalNamespace)
        {
            code.Append("namespace ").Append(model.Namespace).AppendLine();
            code.OpenBlock();
        }

        code.Append(keyword).Append(SparseNaming.EscapeIdentifier(model.Name)).AppendLine();
        code.OpenBlock();
        code.Append(inner);
        code.CloseBlock();
        if (!model.IsGlobalNamespace)
        {
            code.CloseBlock();
        }

        return code.ToString();
    }
}
