using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Orders split-mode Fragment surface public-first.</summary>
/// <remarks>Extracted from <c>SparseFragmentEmitter</c> to keep that file under the size guideline.</remarks>
internal static class SparseFragmentSurfaceOrderingEmitter
{
    /// <summary>Emits the split-mode public fragment tail after Diff.</summary>
    /// <param name="code">Surface target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    /// <param name="runtime">Runtime dialect.</param>
    /// <param name="patchDialect">Patch dialect.</param>
    /// <param name="features">Emission features.</param>
    /// <param name="implementationBuilder">Implementation target.</param>
    /// <param name="jsonConverterQualifiedName">Qualified converter name.</param>
    /// <param name="jsonConverterSimpleName">Simple converter name.</param>
    /// <param name="generatedAccessibility">Fragment accessibility.</param>
    internal static void AppendFragmentPublicTail(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseEmissionFeatures features,
        SharedIndentedBuilder? implementationBuilder,
        string? jsonConverterQualifiedName,
        string? jsonConverterSimpleName,
        string generatedAccessibility,
        string? implementationNamespace = null
    )
    {
        var writableMembers = members.Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly).ToImmutableArray();
        var canWriteInPlace = modelIsReferenceType && writableMembers.Length == members.Length;
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        code.AppendLine("/// <summary>Creates a mutable builder seeded from this fragment.</summary>");
        code.AppendLine("/// <returns>The seeded builder.</returns>");
        code.AppendLine("public FragmentBuilder ToBuilder() => new(this);");
        // JSON converters need both the requested family and a compatible
        // probed compilation surface.
        if (features.EmitJsonConverters && patchDialect.EmitJson)
        {
            if (implementationBuilder is not null && jsonConverterQualifiedName is not null && jsonConverterSimpleName is not null)
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonFacade(code, jsonConverterQualifiedName);
                SparseFragmentJsonEmitter.AppendConverter(
                    implementationBuilder,
                    members,
                    runtime.OptionalType,
                    isStandalone: true,
                    converterClassName: jsonConverterSimpleName,
                    converterAccessibility: "internal",
                    sealedConverter: false,
                    emitFastPaths: patchDialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver,
                    sharedCodecsReference: SharedCodecsReference(implementationBuilder, implementationNamespace)
                );
                implementationBuilder.AppendLine();
            }
            else
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(
                    code,
                    members,
                    runtime.OptionalType,
                    emitFastPaths: patchDialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver
                );
            }
        }
    }

    /// <summary>Shared codec reference for split-mode converters, or null for legacy single-file output.</summary>
    private static string? SharedCodecsReference(SharedIndentedBuilder? implementationBuilder, string? implementationNamespace) =>
        implementationBuilder is not null && implementationNamespace is not null
            ? SparseGeneratedOnceNames.QualifiedJsonCodecs(implementationNamespace)
            : null;

    /// <summary>Emits split-mode Fragment surface in public-first order.</summary>
    /// <remarks>Facade overloads keep public members ahead of internal bridges.</remarks>
    internal static void AppendFragmentSplitSurface(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        SparseFragmentCoreEmitter core,
        SparseFragmentExpressions expressions,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseEmissionFeatures features,
        SharedIndentedBuilder? implementationBuilder,
        string? jsonConverterQualifiedName,
        string? jsonConverterSimpleName,
        string generatedAccessibility,
        string operationsType,
        bool canApplyPatchInPlace,
        string? implementationNamespace = null
    )
    {
        core.AppendDiff(code, modelType, members, modelIsReferenceType, operationsType);
        core.AppendFragmentClonePublic(code, members, usesPocoCloning);
        var writableMembers = members.Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly).ToImmutableArray();
        var canWriteInPlace = modelIsReferenceType && writableMembers.Length == members.Length;
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        code.AppendLine("/// <summary>Creates a mutable builder seeded from this fragment.</summary>");
        code.AppendLine("/// <returns>The seeded builder.</returns>");
        code.AppendLine("public FragmentBuilder ToBuilder() => new(this);");
        if (features.EmitJsonConverters && patchDialect.EmitJson)
        {
            if (implementationBuilder is not null && jsonConverterQualifiedName is not null && jsonConverterSimpleName is not null)
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonAccessor(code, jsonConverterQualifiedName);
                SparseFragmentJsonEmitter.AppendConverter(
                    implementationBuilder,
                    members,
                    runtime.OptionalType,
                    isStandalone: true,
                    converterClassName: jsonConverterSimpleName,
                    converterAccessibility: "internal",
                    sealedConverter: false,
                    emitFastPaths: patchDialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver,
                    sharedCodecsReference: SharedCodecsReference(implementationBuilder, implementationNamespace)
                );
                implementationBuilder.AppendLine();
            }
            else
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(
                    code,
                    members,
                    runtime.OptionalType,
                    emitFastPaths: patchDialect.EffectiveStjCapabilities.SupportsPayloadJsonResolver
                );
            }
        }

        // Internal group trails the public tail.
        core.AppendMemberCaches(
            code,
            members,
            runtime.MergeStrategyType,
            SparseFragmentPatchEmitter.GetRebasePolicyType(patchDialect),
            patchDialect.RebasePolicyField
        );
        core.AppendOriginStorage(code);
        core.AppendOriginFallbackFacades(code, operationsType);
        SparseFragmentCoreEmitter.AppendFromModelInternalFacade(
            code,
            modelType,
            operationsType,
            members.Any(static member => member.ChildModel is not null)
        );
        SparseFragmentCoreEmitter.AppendDiffInternalFacade(
            code,
            modelType,
            operationsType,
            members.Any(static member => member.ChildModel is not null && member.MergeStrategyType is null)
        );
        core.AppendFragmentCloneInternalMethod(code);
        AppendFragmentEquality(code, members, runtime.OptionalType, runtime.OptionalEqualityType, expressions, core);
        var writableMembersSplit = members.Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly).ToImmutableArray();
        var canWriteInPlaceSplit = modelIsReferenceType && writableMembersSplit.Length == members.Length;
        if (canWriteInPlaceSplit || (features.EmitPatch && canApplyPatchInPlace))
        {
            AppendWritableMemberWriter(code, modelType, writableMembersSplit);
        }

        // Private group trails internal helpers with the projection constructor.
        core.AppendFragmentClonePrivateCtor(code, members);
    }

    internal static void AppendWritableMemberWriter(SharedIndentedBuilder code, string modelType, ImmutableArray<SparseMemberModel> members)
    {
        code.AppendLine("internal void __SparseWriteWritableTo(" + modelType + " model)");
        code.OpenBlock();
        code.AppendLine("if (model is null) throw new global::System.ArgumentNullException(nameof(model));");
        foreach (var member in members)
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var source = "this." + property + ".Value!";
            if (member.ChildModel is not null)
            {
                source = member.ChildIsReferenceType ? "this." + property + ".Value?.ToModel()!" : "this." + property + ".Value!.ToModel()";
            }

            code.AppendLine("if (this." + property + ".IsPresent)");
            code.OpenBlock();
            if (member.Collection.Kind == SparseCollectionKind.List && member.Collection.CloneKind == SparseCloneCollectionKind.List)
            {
                var listType = "global::System.Collections.Generic.List<" + member.Collection.ElementType.Name + ">";
                code.AppendLine(
                    "if (model." + property + " is " + listType + " __sparse_list" + member.Id + " && this." + property + ".Value is not null)"
                );
                code.OpenBlock();
                code.AppendLine("__sparse_list" + member.Id + ".Clear();");
                code.AppendLine("__sparse_list" + member.Id + ".AddRange(this." + property + ".Value);");
                code.CloseBlock();
                code.AppendLine("else model." + property + " = " + source + ";");
            }
            else if (member.Collection.IsDictionary)
            {
                var dictionaryType =
                    "global::System.Collections.Generic.Dictionary<"
                    + member.Collection.ElementType.Name
                    + ", "
                    + member.Collection.ValueType!.Value.Name
                    + ">";
                code.AppendLine(
                    "if (model."
                        + property
                        + " is "
                        + dictionaryType
                        + " __sparse_dict"
                        + member.Id
                        + " && this."
                        + property
                        + ".Value"
                        + " is not null)"
                );
                code.OpenBlock();
                code.AppendLine("__sparse_dict" + member.Id + ".Clear();");
                code.AppendLine(
                    "foreach (var __sparse_pair"
                        + member.Id
                        + " in this."
                        + property
                        + ".Value!"
                        + ") __sparse_dict"
                        + member.Id
                        + ".Add(__sparse_pair"
                        + member.Id
                        + ".Key, __sparse_pair"
                        + member.Id
                        + ".Value);"
                );
                code.CloseBlock();
                code.AppendLine("else model." + property + " = " + source + ";");
            }
            else
            {
                code.AppendLine("model." + property + " = " + source + ";");
            }
            code.CloseBlock();
        }

        code.CloseBlock();
    }

    internal static void AppendFragmentEquality(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optionalType,
        string? optionalEqualityType,
        SparseFragmentExpressions expressions,
        SparseFragmentCoreEmitter core
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLine("internal static bool __SparseAreEqual(" + optionalType + "<Fragment?> left, " + optionalType + "<Fragment?> right)");
        code.OpenBlock();
        code.AppendLine("if (!left.IsPresent) return !right.IsPresent;");
        code.AppendLine("if (!right.IsPresent) return false;");
        code.AppendLine("if (global::System.Object.ReferenceEquals(left.Value, right.Value)) return true;");
        code.AppendLine("if (left.Value is null || right.Value is null) return false;");
        if (members.IsEmpty)
        {
            code.AppendLine("return true;");
        }
        else
        {
            code.AppendIndent().Append("return ");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.Append("__SparseEqual_")
                    .Append(members[index].Id)
                    .Append("(left.Value.")
                    .Append(name)
                    .Append(", right.Value.")
                    .Append(name)
                    .Append(")");
            }

            code.AppendLine(";");
        }

        code.CloseBlock();
        foreach (var member in members)
        {
            var valueType = SparseFragmentEmitHelpers.FragmentValueType(member);
            var leftOptional = optionalEqualityType is null ? "left" : "__left";
            var rightOptional = optionalEqualityType is null ? "right" : "__right";
            var leftValue = leftOptional + ".Value";
            var rightValue = rightOptional + ".Value";
            code.AppendIndent()
                .Append("internal static bool __SparseEqual_")
                .Append(member.Id)
                .Append("(")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .Append("> left, ")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .Append("> right) => ");
            string equality;
            if (member.Property.IsTemporaryKey)
            {
                // Temporary identity is correlation metadata, not business
                // value.
                equality = "true";
            }
            else if (member.ChildModel is not null)
            {
                equality = member.ChildFragmentType + ".__SparseAreEqual(" + leftOptional + ", " + rightOptional + ")";
            }
            else if (member.MergeStrategyType is not null)
            {
                equality = core.MergeStrategyField(member) + ".AreEqual(" + leftValue + ", " + rightValue + ")";
            }
            else if (member.ComparisonComparerType is not null)
            {
                equality = SparseFragmentEmitHelpers.ComparisonComparerField(member) + ".Equals(" + leftValue + ", " + rightValue + ")";
            }
            else
            {
                equality = expressions.ValueEqualityExpression(member, leftValue, rightValue);
            }

            if (optionalEqualityType is null)
            {
                code.AppendLine("!left.IsPresent ? !right.IsPresent : right.IsPresent && (" + equality + ");");
            }
            else
            {
                code.AppendLine(optionalEqualityType + ".AreEqual(left, right, static (__left, __right) => " + equality + ");");
            }
        }

        code.AppendLine();
    }
}
