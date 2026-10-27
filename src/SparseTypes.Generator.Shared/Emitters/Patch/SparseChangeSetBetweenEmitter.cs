using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
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

/// <summary>Baseline-aware Between diff dispatch.</summary>
internal static class SparseChangeSetBetweenEmitter
{
    internal static void AppendBetween(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var empty = EmptyArgs(members);
        var emptyName = "__sparse_empty";
        var emptySuffix = 0;
        while (members.Any(member => member.Property.Name == emptyName))
        {
            emptyName = "__sparse_empty" + (++emptySuffix).ToString(CultureInfo.InvariantCulture);
        }

        if (target is not null)
        {
            code.AppendLine("/// <summary>Derives the canonical baseline-aware diff between two states.</summary>");
            code.AppendLine(
                "/// <remarks>The change set snapshots its inputs: whole-root fragments are deep-cloned and member collection containers are copied at capture, so later caller-side mutation cannot alter retained history. Element values are shared by reference. Typed <c>Before</c>/<c>After</c> endpoints return fresh container snapshots for collection members.</remarks>"
            );
            code.AppendLine("/// <param name=\"before\">The state before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The state after the transition.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + optionalFragment
                    + " before, "
                    + optionalFragment
                    + " after) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after, global::System.DateTimeOffset.UtcNow);"
            );
            code.AppendLine("/// <summary>Derives a diff with an explicit recording time.</summary>");
            code.AppendLine("/// <param name=\"before\">The state before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The state after the transition.</param>");
            code.AppendLine("/// <param name=\"recordedAt\">The recording time, or null when it is unknown.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + optionalFragment
                    + " before, "
                    + optionalFragment
                    + " after, global::System.DateTimeOffset? recordedAt) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after, recordedAt);"
            );
            // The algorithm body below streams into the operation container.
            code = target.ChangeSetOperations;
        }
        else
        {
            code.AppendLine("/// <summary>Derives the canonical baseline-aware diff between two states.</summary>");
            code.AppendLine("/// <param name=\"before\">The state before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The state after the transition.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + optionalFragment
                    + " before, "
                    + optionalFragment
                    + " after) => Between(before, after, global::System.DateTimeOffset.UtcNow);"
            );
        }
        // Empty storage contains no retained endpoints or mutable containers.
        code.AppendLine("private static readonly ChangeSet " + emptyName + " = new ChangeSet(" + empty + ");");
        code.AppendLine("/// <summary>Deep-clones a retained whole-root fragment.</summary>");
        code.AppendLine(
            "private static "
                + optionalFragment
                + " __SparseCloneRoot("
                + optionalFragment
                + " value) => !value.IsPresent || value.Value is null ? value : "
                + optionalFragment
                + ".Present(value.Value!.DeepClone());"
        );
        code.AppendLine("/// <summary>Derives the canonical baseline-aware diff between two states.</summary>");
        if (target is null)
        {
            code.AppendLine(
                "/// <remarks>The change set snapshots its inputs: whole-root fragments are deep-cloned and member collection containers are copied at capture, so later caller-side mutation cannot alter retained history. Element values are shared by reference. Typed <c>Before</c>/<c>After</c> endpoints return fresh container snapshots for collection members.</remarks>"
            );
            code.AppendLine("/// <param name=\"before\">The state before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The state after the transition.</param>");
            code.AppendLine("/// <param name=\"recordedAt\">The recording time, or null when it is unknown.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
        }
        code.AppendLine(
            (target is null ? "public static ChangeSet Between(" : "internal static ChangeSet Between(")
                + optionalFragment
                + " before, "
                + optionalFragment
                + " after, global::System.DateTimeOffset? recordedAt)"
        );
        code.OpenBlock();
        code.AppendLine("if (before.IsPresent != after.IsPresent)");
        code.OpenBlock();
        code.AppendLine(
            "return new ChangeSet(true, __SparseCloneRoot(before), __SparseCloneRoot(after), "
                + MemberEmptyTail(members)
                + (members.IsDefaultOrEmpty || members.Length == 0 ? "" : ", ")
                + "recordedAt);"
        );
        code.CloseBlock();
        code.AppendLine("if (!before.IsPresent) return " + emptyName + ".WithRecordedAt(recordedAt);");
        code.AppendLine("if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return " + emptyName + ".WithRecordedAt(recordedAt);");
        code.AppendLine("if (before.Value is null || after.Value is null)");
        code.OpenBlock();
        code.AppendLine("if (before.Value is null && after.Value is null) return " + emptyName + ".WithRecordedAt(recordedAt);");
        code.AppendLine(
            "return new ChangeSet(true, __SparseCloneRoot(before), __SparseCloneRoot(after), "
                + MemberEmptyTail(members)
                + (members.IsDefaultOrEmpty || members.Length == 0 ? "" : ", ")
                + "recordedAt);"
        );
        code.CloseBlock();
        if (members.IsDefaultOrEmpty || members.Length == 0)
        {
            code.AppendLine("return " + emptyName + ".WithRecordedAt(recordedAt);");
            code.CloseBlock();
            return;
        }
        code.AppendLine("var bf = before.Value!;");
        code.AppendLine("var af = after.Value!;");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsNested(member))
            {
                var child = ChildChangeSet(member, dialect);
                code.AppendLine("var __n" + member.Id + " = " + child + ".Between(bf." + name + ", af." + name + ", null);");
                code.AppendLine(child + "? __nn" + member.Id + " = __n" + member.Id + ".IsEmpty ? null : __n" + member.Id + ";");
            }
            else if (IsKeyed(member))
            {
                AppendKeyedBetweenSparse(code, member, name, runtime, dialect);
            }
            else if (IsDict(member))
            {
                AppendDictBetweenSparse(code, member, name, runtime, dialect);
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var beforeAccess = "bf." + name;
                var afterAccess = "af." + name;
                if (SparseChangeSetTransitionEmitter.NeedsSnapshot(member))
                {
                    beforeAccess = "__SparseSnapshot_" + member.Id + "(" + beforeAccess + ")";
                    afterAccess = "__SparseSnapshot_" + member.Id + "(" + afterAccess + ")";
                }
                code.AppendLine("bool __h" + member.Id + " = !Fragment.__SparseEqual_" + member.Id + "(bf." + name + ", af." + name + ");");
                code.AppendLine(opt + " __b" + member.Id + " = __h" + member.Id + " ? " + beforeAccess + " : default;");
                code.AppendLine(opt + " __a" + member.Id + " = __h" + member.Id + " ? " + afterAccess + " : default;");
            }
        }
        // Empty fast path.
        var conds = new List<string>();
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                conds.Add("__nn" + member.Id + " is null");
            }
            else
            {
                conds.Add("!__h" + member.Id);
            }
        }
        code.AppendLine("if (" + string.Join(" && ", conds) + ") return " + emptyName + ".WithRecordedAt(recordedAt);");
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                args.Add("__nn" + member.Id);
            }
            else if (IsKeyed(member))
            {
                AddKeyedChangeSetArgs(
                    args,
                    member,
                    "__h" + member.Id,
                    "__whole" + member.Id,
                    "__wb" + member.Id,
                    "__wa" + member.Id,
                    "__items" + member.Id,
                    "__bO" + member.Id,
                    "__aO" + member.Id,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? "__bTO" + member.Id : null,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? "__aTO" + member.Id : null
                );
            }
            else if (IsDict(member))
            {
                args.AddRange(new[] { "__h" + member.Id, "__whole" + member.Id, "__wb" + member.Id, "__wa" + member.Id, "__items" + member.Id });
            }
            else
            {
                args.AddRange(new[] { "__b" + member.Id, "__a" + member.Id, "__h" + member.Id });
            }
        }
        args.Add("recordedAt");
        code.AppendLine("return new ChangeSet(" + string.Join(", ", args) + ");");
        code.CloseBlock();
    }

    internal static void AppendModelBetween(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType,
        bool modelIsReferenceType,
        string optionalFragment,
        string runtime,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            code.AppendLine("/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>");
            code.AppendLine("/// <param name=\"before\">The model before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The model after the transition.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + modelType
                    + " before, "
                    + modelType
                    + " after) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after, global::System.DateTimeOffset.UtcNow);"
            );
            code.AppendLine("/// <summary>Derives a model diff with an explicit recording time.</summary>");
            code.AppendLine("/// <param name=\"before\">The model before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The model after the transition.</param>");
            code.AppendLine("/// <param name=\"recordedAt\">The recording time, or null when it is unknown.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + modelType
                    + " before, "
                    + modelType
                    + " after, global::System.DateTimeOffset? recordedAt) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after, recordedAt);"
            );
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>");
            code.AppendLine(
                "internal static ChangeSet Between(" + modelType + " before, " + modelType + " after, global::System.DateTimeOffset? recordedAt)"
            );
        }
        else
        {
            code.AppendLine("/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>");
            code.AppendLine("/// <param name=\"before\">The model before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The model after the transition.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between("
                    + modelType
                    + " before, "
                    + modelType
                    + " after) => Between(before, after, global::System.DateTimeOffset.UtcNow);"
            );
            code.AppendLine("/// <summary>Derives a model diff with an explicit recording time.</summary>");
            code.AppendLine("/// <param name=\"before\">The model before the transition.</param>");
            code.AppendLine("/// <param name=\"after\">The model after the transition.</param>");
            code.AppendLine("/// <param name=\"recordedAt\">The recording time, or null when it is unknown.</param>");
            code.AppendLine("/// <returns>The baseline-aware diff.</returns>");
            code.AppendLine(
                "public static ChangeSet Between(" + modelType + " before, " + modelType + " after, global::System.DateTimeOffset? recordedAt)"
            );
        }
        code.OpenBlock();
        if (SparseChangeSetModelBetweenEmitter.CanCompareDirectly(members))
        {
            SparseChangeSetModelBetweenEmitter.AppendBody(code, members, modelIsReferenceType, runtime, optionalFragment, "recordedAt");
        }
        else
        {
            SparseChangeSetModelBetweenEmitter.AppendProjectedReturn(code, optionalFragment, modelIsReferenceType, members, "recordedAt");
        }

        code.CloseBlock();
    }
}
