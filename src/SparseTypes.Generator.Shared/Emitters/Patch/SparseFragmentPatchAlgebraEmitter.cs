using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the standalone patch algebra (Between / Compose / Invert) for a generated patch.</summary>
internal static class SparseFragmentPatchAlgebraEmitter
{
    public static void AppendPatchAlgebra(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        _ = modelType;
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var wholeOperation = runtime + "FragmentOperation<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var expressions = new SparseFragmentExpressions(
            "__sparse_patch_context",
            dialect.RuntimeFacade,
            dialect.RuntimeFacade,
            runtime + "Optional"
        );

        SparseSemanticBetweenEmitter.AppendBetweenMethod(
            code,
            members,
            new SparseBetweenDialect(
                runtime,
                optionalFragment,
                wholeOperation,
                "__sparse_whole",
                prefix,
                dialect.MemberField,
                static member =>
                    member.ChildModel is null
                    && !SparseFragmentPatchEmitter.IsCollectionPatch(member),
                member => SparseFragmentPatchEmitter.GetMemberValueType(dialect, member),
                (member, beforeValue, afterValue) =>
                {
                    // Temporary identity never diffs as a scalar: it is
                    // correlation metadata kept out of business edits.
                    if (member.Property.IsTemporaryKey)
                    {
                        return "true";
                    }

                    if (member.MergeStrategyType is not null)
                    {
                        return "Fragment."
                            + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member)
                            + ".AreEqual("
                            + beforeValue
                            + ", "
                            + afterValue
                            + ")";
                    }

                    if (member.ComparisonComparerType is not null)
                    {
                        return "Fragment."
                            + SparseFragmentEmitHelpers.ComparisonComparerField(member)
                            + ".Equals("
                            + beforeValue
                            + ", "
                            + afterValue
                            + ")";
                    }

                    return expressions.ValueEqualityExpression(member, beforeValue, afterValue);
                },
                (member, before, after) =>
                    SparseFragmentPatchEmitter.IsCollectionPatch(member)
                        ? SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member)
                            + ".Between("
                            + before
                            + ", "
                            + after
                            + ")"
                        : dialect.ChildPatchName(member)
                            + "."
                            + member.ChildModel!.Value.PatchApiPrefix
                            + "Between("
                            + before
                            + ", "
                            + after
                            + ")"
            ),
            target
        );

        AppendCompose(code, members, dialect, prefix, kind, target);
        AppendInvert(code, optionalFragment, prefix, target);
    }

    private static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string prefix,
        string kind,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            code.AppendLine("/// <summary>Composes this patch with a following patch.</summary>");
            code.AppendLine("/// <param name=\"next\">Patch applied after.</param>");
            code.AppendLine("/// <returns>The composed patch.</returns>");
            code.AppendLine(
                "public Patch "
                    + prefix
                    + "Compose(Patch next) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Compose(this, next);"
            );
            code.AppendLine("/// <summary>Composes two patches.</summary>");
            code.AppendLine("/// <param name=\"first\">Patch applied first.</param>");
            code.AppendLine("/// <param name=\"second\">Patch applied after.</param>");
            code.AppendLine("/// <returns>The composed patch.</returns>");
            code.AppendLine(
                "public static Patch "
                    + prefix
                    + "Compose(Patch first, Patch second) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Compose(first, second);"
            );
            AppendComposeBody(target.PatchOperations, members, dialect, prefix, kind, "self.");
            return;
        }

        AppendComposeBody(code, members, dialect, prefix, kind, string.Empty);
        code.AppendLine("/// <summary>Composes two patches.</summary>");
        code.AppendLine("/// <param name=\"first\">Patch applied first.</param>");
        code.AppendLine("/// <param name=\"second\">Patch applied after.</param>");
        code.AppendLine("/// <returns>The composed patch.</returns>");
        code.AppendLine("public static Patch " + prefix + "Compose(Patch first, Patch second)");
        code.OpenBlock();
        code.AppendLine(
            "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
        );
        code.AppendLine("return first." + prefix + "Compose(second);");
        code.CloseBlock();
    }

    private static void AppendComposeBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string prefix,
        string kind,
        string receiver
    )
    {
        var owner = receiver.Length == 0 ? "this" : "self";
        var declaration =
            receiver.Length == 0
                ? "public Patch " + prefix + "Compose(Patch next)"
                : "internal static Patch " + prefix + "Compose(Patch self, Patch next)";
        code.AppendLine("/// <summary>Composes this patch with a following patch.</summary>");
        if (receiver.Length == 0)
        {
            code.AppendLine("/// <param name=\"next\">Patch applied after.</param>");
            code.AppendLine("/// <returns>The composed patch.</returns>");
        }
        code.AppendLine(declaration);
        code.OpenBlock();
        if (receiver.Length != 0)
        {
            code.AppendLine(
                "if (self is null) throw new global::System.ArgumentNullException(nameof(self));"
            );
        }

        code.AppendLine(
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLine("var result = new Patch();");
        code.AppendLine("if (next." + dialect.WholeFieldName + ".Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine(
            "result." + dialect.WholeFieldName + " = next." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            code.AppendLine(
                "result."
                    + dialect.MemberField(member)
                    + " = next."
                    + dialect.MemberField(member)
                    + ";"
            );
        }

        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine(
            "result." + dialect.WholeFieldName + " = " + owner + "." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLine(
                    "result."
                        + field
                        + " = next."
                        + field
                        + ".Kind == "
                        + kind
                        + ".Keep ? "
                        + owner
                        + "."
                        + field
                        + " : next."
                        + field
                        + ";"
                );
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLine(
                    "result."
                        + field
                        + " = next."
                        + field
                        + " is null ? "
                        + owner
                        + "."
                        + field
                        + " : ("
                        + owner
                        + "."
                        + field
                        + " is null ? next."
                        + field
                        + " : "
                        + owner
                        + "."
                        + field
                        + ".Compose(next."
                        + field
                        + "));"
                );
            }
            else
            {
                code.AppendLine(
                    "result."
                        + field
                        + " = next."
                        + field
                        + " is null ? "
                        + owner
                        + "."
                        + field
                        + " : ("
                        + owner
                        + "."
                        + field
                        + " is null ? next."
                        + field
                        + " : "
                        + owner
                        + "."
                        + field
                        + "."
                        + member.ChildModel!.Value.PatchApiPrefix
                        + "Compose(next."
                        + field
                        + "));"
                );
            }
        }

        code.AppendLine("return result;");
        code.CloseBlock();
    }

    private static void AppendInvert(
        SharedIndentedBuilder code,
        string optionalFragment,
        string prefix,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            code.AppendLine("/// <summary>Inverts this patch.</summary>");
            code.AppendLine("/// <param name=\"baseline\">State applied to.</param>");
            code.AppendLine("/// <returns>A patch undoing this patch.</returns>");
            code.AppendLine(
                "public Patch "
                    + prefix
                    + "Invert("
                    + optionalFragment
                    + " baseline) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Invert(this, baseline);"
            );
            var ops = target.PatchOperations;
            ops.AppendLine(
                "/// <summary>Inverts a patch relative to the sparse state it was applied to.</summary>"
            );
            ops.AppendLine(
                "internal static Patch "
                    + prefix
                    + "Invert(Patch self, "
                    + optionalFragment
                    + " baseline)"
            );
            ops.OpenBlock();
            ops.AppendLine("var applied = Apply(self, baseline);");
            ops.AppendLine("return " + prefix + "Between(applied, baseline);");
            ops.CloseBlock();
            return;
        }

        code.AppendLine("/// <summary>Inverts this patch.</summary>");
        code.AppendLine("/// <param name=\"baseline\">State applied to.</param>");
        code.AppendLine("/// <returns>A patch undoing this patch.</returns>");
        code.AppendLine("public Patch " + prefix + "Invert(" + optionalFragment + " baseline)");
        code.OpenBlock();
        code.AppendLine("var applied = this.Apply(baseline);");
        code.AppendLine("return " + prefix + "Between(applied, baseline);");
        code.CloseBlock();
    }
}
