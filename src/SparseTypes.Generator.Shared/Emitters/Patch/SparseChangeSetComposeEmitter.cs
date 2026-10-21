using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Sequential transition composition core.</summary>
internal static class SparseChangeSetComposeEmitter
{
    internal static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        _ = optionalFragment;
        if (target is not null)
        {
            code.AppendLine(
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLine(
                "/// <param name=\"next\">The change set to apply after this change set.</param>"
            );
            code.AppendLine("/// <returns>The composed change set.</returns>");
            code.AppendLine(
                "public ChangeSet Compose(ChangeSet next) => "
                    + target.ChangeSetOperationsType
                    + ".Compose(this, next);"
            );
            code.AppendLine("/// <summary>Composes two sequential change sets.</summary>");
            code.AppendLine("/// <param name=\"first\">The first change set.</param>");
            code.AppendLine("/// <param name=\"second\">The second change set.</param>");
            code.AppendLine("/// <returns>The composed change set.</returns>");
            code.AppendLine(
                "public static ChangeSet Compose(ChangeSet first, ChangeSet second) => "
                    + target.ChangeSetOperationsType
                    + ".Compose(first, second);"
            );
            code = target.ChangeSetOperations;
            code.AppendLine(
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLine("/// <param name=\"self\">The first change set.</param>");
            code.AppendLine("/// <param name=\"next\">The second change set.</param>");
            code.AppendLine("/// <returns>The composed change set.</returns>");
            code.AppendLine("internal static ChangeSet Compose(ChangeSet self, ChangeSet next)");
        }
        else
        {
            code.AppendLine(
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLine(
                "/// <param name=\"next\">The change set to apply after this change set.</param>"
            );
            code.AppendLine("/// <returns>The composed change set.</returns>");
            code.AppendLine("public ChangeSet Compose(ChangeSet next)");
        }
        code.OpenBlock();
        if (target is not null)
        {
            code.AppendLine(
                "if (self is null) throw new global::System.ArgumentNullException(nameof(self));"
            );
            AppendSelfAliases(code, members);
        }
        var __hasSparse = members.Any(static m => IsKeyed(m) || IsDict(m));
        code.AppendLine(
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLine(
            target is null ? "if (IsEmpty) return next;" : "if (self.IsEmpty) return next;"
        );
        code.AppendLine(
            target is null ? "if (next.IsEmpty) return this;" : "if (next.IsEmpty) return self;"
        );
        code.AppendLine("if (__sparse_hasWhole || next.__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine("if (__sparse_hasWhole && next.__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine(
            "if (!Fragment.__SparseAreEqual(__sparse_wholeAfter, next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine("return Between(__sparse_wholeBefore, next.__sparse_wholeAfter);");
        code.CloseBlock();
        // Compose whole-root and memberwise changes when their shared paths are
        // contiguous.
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine(
            "if (!next.__SparseBeforeMatches(__sparse_wholeAfter)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine("var __mergedAfter = next.ToPatch().Apply(__sparse_wholeAfter);");
        code.AppendLine("return Between(__sparse_wholeBefore, __mergedAfter);");
        code.CloseBlock();
        code.AppendLine(
            target is null
                ? "if (!__SparseAfterMatches(next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
                : "if (!self.__SparseAfterMatches(next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine(
            target is null
                ? "var __mergedBefore = Invert().ToPatch().Apply(next.__sparse_wholeBefore);"
                : "var __mergedBefore = Invert(self).ToPatch().Apply(next.__sparse_wholeBefore);"
        );
        code.AppendLine("return Between(__mergedBefore, next.__sparse_wholeAfter);");
        code.CloseBlock();
        if (__hasSparse)
        {
            AppendPragmaDisableNullKey(code);
        }

        foreach (var member in members)
        {
            if (!IsNested(member))
            {
                continue;
            }

            var child = ChildChangeSet(member, dialect);
            code.AppendLine(child + "? __c" + member.Id + ";");
            code.AppendLine(
                "if ("
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = next."
                    + NestedField(member)
                    + ";"
            );
            code.AppendLine(
                "else if (next."
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ";"
            );
            code.AppendLine(
                "else { var __cc"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ".Compose(next."
                    + NestedField(member)
                    + "); __c"
                    + member.Id
                    + " = __cc"
                    + member.Id
                    + ".IsEmpty ? null : __cc"
                    + member.Id
                    + "; }"
            );
        }
        // Emit per-member merge with explicit locals.
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                continue;
            }

            if (IsKeyed(member))
            {
                AppendKeyedComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            if (IsDict(member))
            {
                AppendDictComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            code.AppendLine(opt + " __cb" + member.Id + "_b = default;");
            code.AppendLine(opt + " __cb" + member.Id + "_a = default;");
            code.AppendLine("bool __cb" + member.Id + "_has;");
            code.AppendLine("if (!" + HasField(member) + ")");
            code.OpenBlock();
            code.AppendLine("__cb" + member.Id + "_has = next." + HasField(member) + ";");
            code.AppendLine("__cb" + member.Id + "_b = next." + BeforeField(member) + ";");
            code.AppendLine("__cb" + member.Id + "_a = next." + AfterField(member) + ";");
            code.CloseBlock();
            code.AppendLine("else if (!next." + HasField(member) + ")");
            code.OpenBlock();
            code.AppendLine("__cb" + member.Id + "_has = true;");
            code.AppendLine("__cb" + member.Id + "_b = " + BeforeField(member) + ";");
            code.AppendLine("__cb" + member.Id + "_a = " + AfterField(member) + ";");
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "if (!Fragment.__SparseEqual_"
                    + member.Id
                    + "("
                    + AfterField(member)
                    + ", next."
                    + BeforeField(member)
                    + ")) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLine(
                "if (Fragment.__SparseEqual_"
                    + member.Id
                    + "("
                    + BeforeField(member)
                    + ", next."
                    + AfterField(member)
                    + ")) { __cb"
                    + member.Id
                    + "_has = false; }"
            );
            code.AppendLine(
                "else { __cb"
                    + member.Id
                    + "_has = true; __cb"
                    + member.Id
                    + "_b = "
                    + BeforeField(member)
                    + "; __cb"
                    + member.Id
                    + "_a = next."
                    + AfterField(member)
                    + "; }"
            );
            code.CloseBlock();
        }
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                args.Add("__c" + member.Id);
            }
            else if (IsKeyed(member))
            {
                AddKeyedChangeSetArgs(
                    args,
                    member,
                    "__cb" + member.Id + "_has",
                    "__cb" + member.Id + "_whole",
                    "__cb" + member.Id + "_wb",
                    "__cb" + member.Id + "_wa",
                    "__cb" + member.Id + "_items",
                    "__cb" + member.Id + "_bO",
                    "__cb" + member.Id + "_aO",
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? "__cb" + member.Id + "_tbO"
                        : null,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? "__cb" + member.Id + "_taO"
                        : null
                );
            }
            else if (IsDict(member))
            {
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_has",
                        "__cb" + member.Id + "_whole",
                        "__cb" + member.Id + "_wb",
                        "__cb" + member.Id + "_wa",
                        "__cb" + member.Id + "_items",
                    }
                );
            }
            else
            {
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_b",
                        "__cb" + member.Id + "_a",
                        "__cb" + member.Id + "_has",
                    }
                );
            }
        }
        if (__hasSparse)
        {
            AppendPragmaRestoreNullKey(code);
        }

        code.AppendLine("return new ChangeSet(" + string.Join(", ", args) + ");");
        code.CloseBlock();
        if (target is null)
        {
            code.AppendLine("/// <summary>Composes two sequential change sets.</summary>");
            code.AppendLine("/// <param name=\"first\">The first change set.</param>");
            code.AppendLine("/// <param name=\"second\">The second change set.</param>");
            code.AppendLine("/// <returns>The composed change set.</returns>");
            code.AppendLine("public static ChangeSet Compose(ChangeSet first, ChangeSet second)");
            code.OpenBlock();
            code.AppendLine(
                "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
            );
            code.AppendLine("return first." + "Compose(second);");
            code.CloseBlock();
        }
    }

    /// <summary>Emits per-key sparse compose for a keyed member.</summary>
    /// <remarks>Stores sparse compose state in generated locals.</remarks>
    internal static void AppendPragmaDisableNullKey(SharedIndentedBuilder code)
    {
        // Use comparer-aware key maps for all key shapes.
        code.AppendLine(
            "#pragma warning disable CS8604 // Sparse stable keys are non-null by construction."
        );
    }

    internal static void AppendPragmaRestoreNullKey(SharedIndentedBuilder code)
    {
        code.AppendLine("#pragma warning restore CS8604");
    }
}
