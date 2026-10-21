using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits merge/diff/apply helpers for generated fragments.</summary>
internal sealed class SparseFragmentMergeEmitter
{
    private string Optional { get; }
    private string MergeStrategyFieldPrefix { get; }
    private string ReferenceComparer { get; }
    private SparseFragmentExpressions Expressions { get; }

    public SparseFragmentMergeEmitter(
        string optional,
        string mergeStrategyFieldPrefix,
        string referenceComparer,
        SparseFragmentExpressions expressions,
        string fieldQualifier = "",
        SparseFragmentOriginEmitter? originEmitter = null
    )
    {
        Optional = optional;
        MergeStrategyFieldPrefix = mergeStrategyFieldPrefix;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
        FieldQualifier = fieldQualifier;
        Origins = originEmitter;
    }

    private string FieldQualifier { get; }

    private SparseFragmentOriginEmitter? Origins { get; }

    private string MergeStrategyField(SparseMemberModel member) =>
        FieldQualifier
        + SparseFragmentEmitHelpers.MergeStrategyField(MergeStrategyFieldPrefix, member);

    public void AppendMerge(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    )
    {
        // Stage 4: with an operations target the surface keeps a one-line
        // facade.
        if (operationsType is not null)
        {
            code.AppendLine(
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            code.AppendLine(
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLine("/// <returns>The merged fragment.</returns>");
            code.AppendLine(
                "public Fragment Merge(Fragment higherPriority) => "
                    + operationsType
                    + ".Merge(this, higherPriority);"
            );

            return;
        }
        var hasCustomMergeStrategy = false;
        var replaceOnly = members.Length > 0;
        foreach (var member in members)
        {
            if (member.MergeStrategyType is not null)
            {
                hasCustomMergeStrategy = true;
                replaceOnly = false;
                continue;
            }

            if (
                SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null)
                || member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
            )
            {
                replaceOnly = false;
            }
        }

        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (Origins is null)
        {
            code.AppendLine(
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            if (isOperationsBody)
            {
                code.AppendLine("/// <param name=\"self\">The lower-priority fragment.</param>");
            }

            code.AppendLine(
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLine("/// <returns>The merged fragment.</returns>");
            if (isOperationsBody)
            {
                code.AppendLine(
                    "public static Fragment Merge(Fragment self, Fragment higherPriority)"
                );
            }
            else
            {
                code.AppendLine("public Fragment Merge(Fragment higherPriority)");
            }

            code.OpenBlock();
            if (isOperationsBody)
            {
                SparseFragmentEmitHelpers.AppendNullGuard(code, "self");
            }

            SparseFragmentEmitHelpers.AppendNullGuard(code, "higherPriority");
            AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
            code.AppendLine("return new Fragment");
            code.OpenBlock();
            for (var index = 0; index < members.Length; index++)
            {
                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.AppendIndent()
                    .Append(name)
                    .Append(" = ")
                    .Append(BuildMergeValueExpression(members[index], receiver, index))
                    .AppendLine(",");
            }

            code.AppendLine("};");
            code.CloseBlock();
            code.AppendLine();
            return;
        }

        if (isOperationsBody)
        {
            code.AppendLine(
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            code.AppendLine("/// <param name=\"self\">The lower-priority fragment.</param>");
            code.AppendLine(
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLine(
                "/// <param name=\"selfFallback\">The fallback origin for the lower contribution.</param>"
            );
            code.AppendLine(
                "/// <param name=\"higherFallback\">The fallback origin for the higher contribution.</param>"
            );
            code.AppendLine("/// <returns>The merged fragment.</returns>");
            code.AppendLine(
                "public static Fragment Merge(Fragment self, Fragment higherPriority, string? selfFallback = null, string? higherFallback = null)"
            );
            code.OpenBlock();
            SparseFragmentEmitHelpers.AppendNullGuard(code, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "higherPriority");
            AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
            SparseFragmentMergeOriginEmitter.AppendMergeWithOrigins(
                code,
                members,
                receiver,
                BuildMergeValueExpression,
                Origins
            );
            code.CloseBlock();
            code.AppendLine();
            return;
        }

        code.AppendLine(
            "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
        );
        code.AppendLine(
            "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
        );
        code.AppendLine("/// <returns>The merged fragment.</returns>");
        code.AppendLine(
            "public Fragment Merge(Fragment higherPriority) => __SparseMergeWithFallback(higherPriority, null, null);"
        );
        code.AppendLine(
            "/// <summary>Merges with explicit fallback origins for nested attribution.</summary>"
        );
        code.AppendLine(
            "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
        );
        code.AppendLine(
            "/// <param name=\"selfFallback\">The fallback origin for this contribution.</param>"
        );
        code.AppendLine(
            "/// <param name=\"higherFallback\">The fallback origin for the higher contribution.</param>"
        );
        code.AppendLine("/// <returns>The merged fragment.</returns>");
        code.AppendLine(
            "internal Fragment __SparseMergeWithFallback(Fragment higherPriority, string? selfFallback, string? higherFallback)"
        );
        code.OpenBlock();
        SparseFragmentEmitHelpers.AppendNullGuard(code, "higherPriority");
        AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
        SparseFragmentMergeOriginEmitter.AppendMergeWithOrigins(
            code,
            members,
            receiver,
            BuildMergeValueExpression,
            Origins
        );
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendMergeFastPaths(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string receiver,
        bool hasCustomMergeStrategy,
        bool replaceOnly
    )
    {
        if (!hasCustomMergeStrategy)
        {
            code.AppendLine("if (higherPriority.IsEmpty)");
            code.OpenBlock();
            code.AppendLine("return " + receiver.TrimEnd('.') + ";");
            code.CloseBlock();
            code.AppendLine("if (" + receiver + "IsEmpty)");
            code.OpenBlock();
            code.AppendLine("return higherPriority;");
            code.CloseBlock();
        }

        if (replaceOnly)
        {
            code.AppendIndent().Append("if (");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                code.Append("higherPriority.")
                    .Append(SparseNaming.EscapeIdentifier(members[index].Property.Name))
                    .Append(".IsPresent");
            }

            code.AppendLine(")");
            code.OpenBlock();
            code.AppendLine("return higherPriority;");
            code.CloseBlock();
        }
    }

    private string BuildMergeValueExpression(
        SparseMemberModel member,
        string receiver,
        int position
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var lower = receiver + name;
        var higher = "higherPriority." + name;
        if (member.MergeStrategyType is not null)
        {
            return $"{MergeStrategyField(member)}.Merge({lower}, {higher})";
        }

        if (SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null))
        {
            // Default deep-merges nested models and replaces other values.
            // Missing preserves the lower value; present null replaces it.
            var merge =
                Origins is not null && SparseFragmentOriginEmitter.IsFragmentDeep(member)
                    ? $"{lower}.Value!.__SparseMergeWithFallback({higher}.Value!, {SparseFragmentOriginEmitter.MemberAttribution(receiver, position, "selfFallback")}, {SparseFragmentOriginEmitter.MemberAttribution("higherPriority.", position, "higherFallback")})"
                    : $"{lower}.Value!.Merge({higher}.Value!)";
            return $"{higher}.IsPresent ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present(({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null) ? {merge} : {higher}.Value) : {lower}";
        }

        if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
        {
            var merged = Expressions.BuildCollectionMerge(
                member,
                lower + ".Value!",
                higher + ".Value!"
            );
            return $"{higher}.IsPresent ? ({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present({merged}) : {higher}) : {lower}";
        }

        return $"{higher}.IsPresent ? {higher} : {lower}";
    }

    public void AppendApplyChanges(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    )
    {
        if (operationsType is not null)
        {
            code.AppendLine(
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            code.AppendLine("/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLine("/// <returns>The fragment with the diff applied.</returns>");
            code.AppendLine(
                "public Fragment ApplyChanges(Fragment changes) => "
                    + operationsType
                    + ".ApplyChanges(this, changes);"
            );

            return;
        }

        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (Origins is null)
        {
            code.AppendLine(
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            if (isOperationsBody)
            {
                code.AppendLine("/// <param name=\"self\">The fragment to update.</param>");
            }

            code.AppendLine("/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLine("/// <returns>The fragment with the diff applied.</returns>");
            if (isOperationsBody)
            {
                code.AppendLine(
                    "public static Fragment ApplyChanges(Fragment self, Fragment changes)"
                );
            }
            else
            {
                code.AppendLine("public Fragment ApplyChanges(Fragment changes)");
            }

            code.OpenBlock();
            if (isOperationsBody)
            {
                SparseFragmentEmitHelpers.AppendNullGuard(code, "self");
            }

            SparseFragmentEmitHelpers.AppendNullGuard(code, "changes");
            code.AppendLine("return new Fragment");
            code.OpenBlock();
            for (var index = 0; index < members.Length; index++)
            {
                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.AppendIndent()
                    .Append(name)
                    .Append(" = ")
                    .Append(BuildApplyValueExpression(members[index], receiver, index))
                    .AppendLine(",");
            }

            code.AppendLine("};");
            code.CloseBlock();
            code.AppendLine();
            return;
        }

        if (isOperationsBody)
        {
            code.AppendLine(
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            code.AppendLine("/// <param name=\"self\">The fragment to update.</param>");
            code.AppendLine("/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLine(
                "/// <param name=\"selfFallback\">The fallback origin for the updated fragment.</param>"
            );
            code.AppendLine(
                "/// <param name=\"changesFallback\">The fallback origin for the diff.</param>"
            );
            code.AppendLine("/// <returns>The fragment with the diff applied.</returns>");
            code.AppendLine(
                "public static Fragment ApplyChanges(Fragment self, Fragment changes, string? selfFallback = null, string? changesFallback = null)"
            );
            code.OpenBlock();
            SparseFragmentEmitHelpers.AppendNullGuard(code, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "changes");
            SparseFragmentMergeOriginEmitter.AppendApplyChangesWithOrigins(
                code,
                members,
                receiver,
                BuildApplyValueExpression
            );
            code.CloseBlock();
            code.AppendLine();
            return;
        }

        code.AppendLine(
            "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
        );
        code.AppendLine("/// <param name=\"changes\">The diff to apply.</param>");
        code.AppendLine("/// <returns>The fragment with the diff applied.</returns>");
        code.AppendLine(
            "public Fragment ApplyChanges(Fragment changes) => __SparseApplyChangesWithFallback(changes, null, null);"
        );
        code.AppendLine(
            "/// <summary>Applies a diff with explicit fallback origins for nested attribution.</summary>"
        );
        code.AppendLine("/// <param name=\"changes\">The diff to apply.</param>");
        code.AppendLine(
            "/// <param name=\"selfFallback\">The fallback origin for the updated fragment.</param>"
        );
        code.AppendLine(
            "/// <param name=\"changesFallback\">The fallback origin for the diff.</param>"
        );
        code.AppendLine("/// <returns>The fragment with the diff applied.</returns>");
        code.AppendLine(
            "internal Fragment __SparseApplyChangesWithFallback(Fragment changes, string? selfFallback, string? changesFallback)"
        );
        code.OpenBlock();
        SparseFragmentEmitHelpers.AppendNullGuard(code, "changes");
        SparseFragmentMergeOriginEmitter.AppendApplyChangesWithOrigins(
            code,
            members,
            receiver,
            BuildApplyValueExpression
        );
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the internal origin-fallback facades for split emission.</summary>
    /// <remarks>Called with the internal caches so public facades precede them.</remarks>
    /// <param name="code">Surface target builder.</param>
    /// <param name="operationsType">Operations class qualifier.</param>
    public void AppendOriginFallbackFacades(SharedIndentedBuilder code, string operationsType)
    {
        if (Origins is null)
        {
            return;
        }

        code.AppendLine(
            "internal Fragment __SparseMergeWithFallback(Fragment higherPriority, string? selfFallback, string? higherFallback) => "
                + operationsType
                + ".Merge(this, higherPriority, selfFallback, higherFallback);"
        );
        code.AppendLine(
            "internal Fragment __SparseApplyChangesWithFallback(Fragment changes, string? selfFallback, string? changesFallback) => "
                + operationsType
                + ".ApplyChanges(this, changes, selfFallback, changesFallback);"
        );
    }

    private string BuildApplyValueExpression(
        SparseMemberModel member,
        string receiver,
        int position
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var type = SparseFragmentEmitHelpers.FragmentValueType(member);
        var self = receiver + name;
        if (member.ChildModel is null)
        {
            return $"changes.{name}.IsPresent ? changes.{name} : {self}";
        }

        // With origins, nested fragments inherit the enclosing default
        // through the fallback seam unless explicitly originated.
        var apply =
            Origins is not null && SparseFragmentOriginEmitter.IsFragmentDeep(member)
                ? $"{self}.Value!.__SparseApplyChangesWithFallback(changes.{name}.Value!, {SparseFragmentOriginEmitter.MemberAttribution(receiver, position, "selfFallback")}, {SparseFragmentOriginEmitter.MemberAttribution("changes.", position, "changesFallback")})"
                : $"{self}.Value!.ApplyChanges(changes.{name}.Value!)";
        return $"changes.{name}.IsPresent ? {Optional}<{type}>.Present(({self}.IsPresent && (object?){self}.Value is not null && (object?)changes.{name}.Value is not null) ? {apply} : changes.{name}.Value) : {self}";
    }

    public void AppendDiff(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        string? operationsType = null
    )
    {
        // Stage 4: facades delegate both Diff overloads; the core and
        // per-member helpers move with no surface counterpart.
        if (operationsType is not null)
        {
            code.AppendLine(
                "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
            );
            code.AppendLine("/// <param name=\"before\">The baseline value.</param>");
            code.AppendLine("/// <param name=\"after\">The updated value.</param>");
            code.AppendLine("/// <returns>The sparse diff from baseline to updated.</returns>");
            code.AppendIndent()
                .Append("public static Fragment Diff(")
                .Append(modelType)
                .Append(" before, ")
                .Append(modelType)
                .Append(" after) => ")
                .Append(operationsType)
                .AppendLine(".Diff(before, after);");
            return;
        }
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        var requiresDiffContext = members.Any(static member =>
            member.ChildModel is not null && member.MergeStrategyType is null
        );
        // Public-first ordering: buffer the per-member private helpers and
        // append them after the public/internal overloads below.
        using var memberHelpers = new SharedIndentedBuilder(code.CancellationToken);
        var surface = code;
        code = memberHelpers;
        foreach (
            var member in members.Where(member =>
                requiresDiffContext && member.ChildModel is not null
            )
        )
        {
            var type = member.ChildModel!.Value.NonNullableName;
            var fragment = member.ChildFragmentType!;
            code.AppendIndent()
                .Append("private static ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> __Diff_")
                .Append(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('(');
            if (!member.ChildIsReferenceType)
            {
                code.Append(type).Append(" before, ").Append(type).Append(" after");
                if (member.ChildRequiresDiffCycleContext)
                {
                    code.Append(", ")
                        .Append(diffContextType)
                        .Append(" __sparse_diff_context, string __sparse_diff_path");
                }
                code.AppendLine(")");
                code.OpenBlock();
                code.AppendIndent()
                    .Append("if (global::System.Collections.Generic.EqualityComparer<")
                    .Append(type)
                    .AppendLine(">.Default.Equals(before!, after!)) { return default; }");
                code.AppendIndent()
                    .Append("return ")
                    .Append(Optional)
                    .Append("<")
                    .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                    .Append(">.Present(")
                    .Append(fragment)
                    .AppendLine(
                        member.ChildRequiresDiffCycleContext
                            ? ".Diff(before, after, __sparse_diff_context, __sparse_diff_path));"
                            : ".Diff(before, after));"
                    );
                code.CloseBlock();
                continue;
            }

            code.Append(type).Append("? before, ").Append(type).Append("? after");
            if (member.ChildRequiresDiffCycleContext)
            {
                code.Append(", ")
                    .Append(diffContextType)
                    .Append(" __sparse_diff_context, string __sparse_diff_path");
            }
            code.AppendLine(")");
            code.OpenBlock();
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return default; }"
            );
            code.AppendIndent()
                .Append("if (before is null || after is null) { return ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(after is null ? null : ")
                .Append(fragment)
                .AppendLine(".From(after)); }");
            code.AppendIndent()
                .Append("var difference = ")
                .Append(fragment)
                .AppendLine(
                    member.ChildRequiresDiffCycleContext
                        ? ".Diff(before, after, __sparse_diff_context, __sparse_diff_path);"
                        : ".Diff(before, after);"
                );
            code.AppendIndent()
                .Append("return difference.IsEmpty ? default : ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .AppendLine(">.Present(difference);");
            code.CloseBlock();
        }

        code = surface;
        code.AppendLine(
            "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
        );
        code.AppendLine("/// <param name=\"before\">The baseline value.</param>");
        code.AppendLine("/// <param name=\"after\">The updated value.</param>");
        code.AppendLine("/// <returns>The sparse diff from baseline to updated.</returns>");
        code.AppendIndent()
            .Append("public static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .AppendLine(" after)");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "after");
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        if (requiresDiffContext)
        {
            code.AppendLine(
                "var __sparse_diff_context = " + ReferenceComparer + ".CreateDiffCycleContext();"
            );
            code.AppendLine("return __SparseDiffCore(before, after, __sparse_diff_context, \"\");");
        }
        else
        {
            AppendDiffBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
        if (!requiresDiffContext)
        {
            return;
        }
        code.AppendIndent()
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after")
            .Append(", ")
            .Append(diffContextType)
            .Append(" __sparse_diff_context, string __sparse_diff_path");
        code.AppendLine(")");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "after");
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        code.AppendLine(
            "return __SparseDiffCore(before, after, __sparse_diff_context, __sparse_diff_path);"
        );
        code.CloseBlock();
        code.AppendLine();
        code.Append(memberHelpers.ToString());
        code.AppendLine();
        code.AppendIndent()
            .Append("private static Fragment __SparseDiffCore(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            code.AppendLine(
                "var __sparse_diff_pair = new global::System.Collections.Generic.KeyValuePair<object, object>((object)before, (object)after);"
            );
            code.AppendLine("if (!__sparse_diff_context.Add(__sparse_diff_pair))");
            code.OpenBlock();
            code.AppendLine(
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during Diff at '\" + __sparse_diff_path + \"'. Fragment.Diff does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.CloseBlock();
            code.AppendLine("try");
            code.OpenBlock();
            AppendDiffBody(code, members);
            code.CloseBlock();
            code.AppendLine("finally");
            code.OpenBlock();
            code.AppendLine("__sparse_diff_context.Remove(__sparse_diff_pair);");
            code.CloseBlock();
        }
        else
        {
            AppendDiffBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the public Diff body preceding internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDiffPublicBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        code.AppendLine(
            "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
        );
        code.AppendLine("/// <param name=\"before\">The baseline value.</param>");
        code.AppendLine("/// <param name=\"after\">The updated value.</param>");
        code.AppendLine("/// <returns>The sparse diff from baseline to updated.</returns>");
        code.AppendIndent()
            .Append("public static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .AppendLine(" after)");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "after");
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        var requiresDiffContext = members.Any(static member =>
            member.ChildModel is not null && member.MergeStrategyType is null
        );
        if (requiresDiffContext)
        {
            code.AppendLine(
                "var __sparse_diff_context = " + ReferenceComparer + ".CreateDiffCycleContext();"
            );
            code.AppendLine("return __SparseDiffCore(before, after, __sparse_diff_context, \"\");");
        }
        else
        {
            AppendDiffBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the internal Diff body trailing public operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public static void AppendDiffInternalBody(
        SharedIndentedBuilder code,
        string modelType,
        bool modelIsReferenceType,
        bool requiresDiffContext
    )
    {
        if (!requiresDiffContext)
        {
            return;
        }
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        code.AppendIndent()
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after")
            .Append(", ")
            .Append(diffContextType)
            .Append(" __sparse_diff_context, string __sparse_diff_path");
        code.AppendLine(")");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, "after");
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        code.AppendLine(
            "return __SparseDiffCore(before, after, __sparse_diff_context, __sparse_diff_path);"
        );
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits private Diff helpers trailing internal operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDiffPrivateBodies(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        if (
            !members.Any(static member =>
                member.ChildModel is not null && member.MergeStrategyType is null
            )
        )
        {
            return;
        }
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        foreach (var member in members.Where(static member => member.ChildModel is not null))
        {
            var type = member.ChildModel!.Value.NonNullableName;
            var fragment = member.ChildFragmentType!;
            code.AppendIndent()
                .Append("private static ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> __Diff_")
                .Append(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('(');
            if (!member.ChildIsReferenceType)
            {
                code.Append(type).Append(" before, ").Append(type).Append(" after");
                if (member.ChildRequiresDiffCycleContext)
                {
                    code.Append(", ")
                        .Append(diffContextType)
                        .Append(" __sparse_diff_context, string __sparse_diff_path");
                }
                code.AppendLine(")");
                code.OpenBlock();
                code.AppendIndent()
                    .Append("if (global::System.Collections.Generic.EqualityComparer<")
                    .Append(type)
                    .AppendLine(">.Default.Equals(before!, after!)) { return default; }");
                code.AppendIndent()
                    .Append("return ")
                    .Append(Optional)
                    .Append("<")
                    .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                    .Append(">.Present(")
                    .Append(fragment)
                    .AppendLine(
                        member.ChildRequiresDiffCycleContext
                            ? ".Diff(before, after, __sparse_diff_context, __sparse_diff_path));"
                            : ".Diff(before, after));"
                    );
                code.CloseBlock();
                continue;
            }

            code.Append(type).Append("? before, ").Append(type).Append("? after");
            if (member.ChildRequiresDiffCycleContext)
            {
                code.Append(", ")
                    .Append(diffContextType)
                    .Append(" __sparse_diff_context, string __sparse_diff_path");
            }
            code.AppendLine(")");
            code.OpenBlock();
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, after)) { return default; }"
            );
            code.AppendIndent()
                .Append("if (before is null || after is null) { return ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(after is null ? null : ")
                .Append(fragment)
                .AppendLine(".From(after)); }");
            code.AppendIndent()
                .Append("var difference = ")
                .Append(fragment)
                .AppendLine(
                    member.ChildRequiresDiffCycleContext
                        ? ".Diff(before, after, __sparse_diff_context, __sparse_diff_path);"
                        : ".Diff(before, after);"
                );
            code.AppendIndent()
                .Append("return difference.IsEmpty ? default : ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .AppendLine(">.Present(difference);");
            code.CloseBlock();
        }

        code.AppendLine();
        code.AppendIndent()
            .Append("private static Fragment __SparseDiffCore(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            code.AppendLine(
                "var __sparse_diff_pair = new global::System.Collections.Generic.KeyValuePair<object, object>((object)before, (object)after);"
            );
            code.AppendLine("if (!__sparse_diff_context.Add(__sparse_diff_pair))");
            code.OpenBlock();
            code.AppendLine(
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during Diff at '\" + __sparse_diff_path + \"'. Fragment.Diff does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.CloseBlock();
            code.AppendLine("try");
            code.OpenBlock();
            AppendDiffBody(code, members);
            code.CloseBlock();
            code.AppendLine("finally");
            code.OpenBlock();
            code.AppendLine("__sparse_diff_context.Remove(__sparse_diff_pair);");
            code.CloseBlock();
        }
        else
        {
            AppendDiffBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    private void AppendDiffBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLine("return new Fragment");
        code.OpenBlock();
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var before = "before." + name;
            var after = "after." + name;
            var valueType = SparseFragmentEmitHelpers.FragmentValueType(member);
            string condition;
            if (member.MergeStrategyType is not null)
            {
                condition =
                    $"{MergeStrategyField(member)}.AreEqual({before}, {after}) ? default : {Optional}<{valueType}>.Present({after})";
            }
            else if (member.ChildModel is null)
            {
                condition =
                    $"{Expressions.ValueEqualityExpression(member, before, after)} ? default : {Optional}<{valueType}>.Present({after})";
            }
            else
            {
                condition = member.ChildRequiresDiffCycleContext
                    ? $"__Diff_{member.Id}({before}, {after}, __sparse_diff_context, {DiffMemberPath(member)})"
                    : $"__Diff_{member.Id}({before}, {after})";
            }

            code.AppendIndent().Append(name).Append(" = ").Append(condition).AppendLine(",");
        }

        code.AppendLine("};");
    }

    private static string DiffMemberPath(SparseMemberModel member)
    {
        var escaped = member.Property.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "(__sparse_diff_path.Length == 0 ? \""
            + escaped
            + "\" : __sparse_diff_path + \"."
            + escaped
            + "\")";
    }

    /// <summary>Emits the internal Diff facade trailing the public fragment surface.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="operationsType">Operations class qualifying the delegate.</param>
    public static void AppendDiffInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType,
        bool requiresDiffContext
    )
    {
        if (!requiresDiffContext)
        {
            return;
        }
        code.AppendIndent()
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(
                " after, global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>> __sparse_diff_context, string __sparse_diff_path) => "
            )
            .Append(operationsType)
            .AppendLine(".Diff(before, after, __sparse_diff_context, __sparse_diff_path);");
    }
}
