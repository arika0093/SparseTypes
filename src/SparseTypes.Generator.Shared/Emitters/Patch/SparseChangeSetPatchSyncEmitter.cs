using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
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
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Patch attach/project (FromPatch/ToPatch) plus direction inversion.</summary>
internal static class SparseChangeSetPatchSyncEmitter
{
    internal static void AppendFromPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optionalFragment,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        _ = members;
        var shell = code;
        if (target is not null)
        {
            code.AppendLine("/// <summary>Attaches a known baseline to an arbitrary patch.</summary>");
            code.AppendLine("/// <param name=\"baseline\">The known baseline.</param>");
            code.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
            code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
            code.AppendLine(
                "public static ChangeSet FromPatch("
                    + optionalFragment
                    + " baseline, Patch patch) => "
                    + target.ChangeSetOperationsType
                    + ".FromPatch(baseline, patch);"
            );
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Attaches a known baseline to an arbitrary patch.</summary>");
            code.AppendLine("/// <param name=\"baseline\">The known baseline.</param>");
            code.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
            code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
            code.AppendLine("internal static ChangeSet FromPatch(" + optionalFragment + " baseline, Patch patch)");
        }
        else
        {
            code.AppendLine("/// <summary>Attaches a known baseline to an arbitrary patch.</summary>");
            code.AppendLine("/// <param name=\"baseline\">The known baseline.</param>");
            code.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
            code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
            code.AppendLine("public static ChangeSet FromPatch(" + optionalFragment + " baseline, Patch patch)");
        }
        code.OpenBlock();
        code.AppendLine("if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));");
        code.AppendLine("var after = patch.Apply(baseline);");
        code.AppendLine("return Between(baseline, after);");
        code.CloseBlock();
        if (modelType is not null)
        {
            if (target is not null)
            {
                // NOTE: code already streams into the operation container here.
                code.AppendLine("/// <summary>Attaches an ordinary model baseline to an arbitrary patch.</summary>");
                code.AppendLine("/// <param name=\"baseline\">The model baseline.</param>");
                code.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
                code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
                code.AppendLine("internal static ChangeSet FromPatch(" + modelType + " baseline, Patch patch)");
            }
            else
            {
                code.AppendLine("/// <summary>Attaches an ordinary model baseline to an arbitrary patch.</summary>");
                code.AppendLine("/// <param name=\"baseline\">The model baseline.</param>");
                code.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
                code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
                code.AppendLine("public static ChangeSet FromPatch(" + modelType + " baseline, Patch patch)");
            }
            code.OpenBlock();
            code.AppendLine("return FromPatch(" + optionalFragment + ".Present(Fragment.From(baseline)), patch);");
            code.CloseBlock();
            if (target is not null)
            {
                shell.AppendLine("/// <summary>Attaches an ordinary model baseline to an arbitrary patch.</summary>");
                shell.AppendLine("/// <param name=\"baseline\">The model baseline.</param>");
                shell.AppendLine("/// <param name=\"patch\">The patch to attach.</param>");
                shell.AppendLine("/// <returns>The baseline-aware change set.</returns>");
                shell.AppendLine(
                    "public static ChangeSet FromPatch("
                        + modelType
                        + " baseline, Patch patch) => "
                        + target.ChangeSetOperationsType
                        + ".FromPatch(baseline, patch);"
                );
            }
        }
    }

    internal static void AppendToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string prefix,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var between = "Patch." + prefix + "Between";
        if (target is not null)
        {
            code.AppendLine("/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>");
            code.AppendLine("/// <returns>The baseline-free patch.</returns>");
            code.AppendLine("public Patch ToPatch() => " + target.ChangeSetOperationsType + ".ToPatch(this);");
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>");
            code.AppendLine("/// <param name=\"self\">The change set to project.</param>");
            code.AppendLine("/// <returns>The baseline-free patch.</returns>");
            code.AppendLine("internal static Patch ToPatch(ChangeSet self)");
        }
        else
        {
            code.AppendLine("/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>");
            code.AppendLine("/// <returns>The baseline-free patch.</returns>");
            code.AppendLine("public Patch ToPatch()");
        }
        code.OpenBlock();
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        code.AppendLine("if (__sparse_hasWhole) return " + between + "(__sparse_wholeBefore, __sparse_wholeAfter);");
        code.AppendLine("var patch = new Patch();");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsNested(member))
            {
                code.AppendLine("if (" + NestedField(member) + " is not null)");
                code.OpenBlock();
                code.AppendLine("patch." + name + " = " + NestedField(member) + ".ToPatch();");
                code.CloseBlock();
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                AppendKeyedDictToPatch(code, member, name, dialect, selfName: target is null ? null : "self");
            }
            else
            {
                var vt = SparseFragmentPatchEmitter.GetMemberValueType(dialect, member);
                var op = runtime + "FragmentOperation<" + vt + ">";
                code.AppendLine("if (" + HasField(member) + ")");
                code.OpenBlock();
                code.AppendLine(
                    "patch."
                        + name
                        + " = "
                        + AfterField(member)
                        + ".IsPresent ? "
                        + op
                        + ".Set("
                        + AfterField(member)
                        + ".Value) : "
                        + op
                        + ".Remove;"
                );
                code.CloseBlock();
            }
        }
        code.AppendLine("return patch;");
        code.CloseBlock();
        foreach (var member in members)
        {
            if (!IsDict(member))
            {
                continue;
            }
            code.AppendLine(
                (target is null ? "private int __SparseRemovalCount" : "internal static int __SparseRemovalCount")
                    + member.Id
                    + (target is null ? "()" : "(ChangeSet self)")
            );
            code.OpenBlock();
            if (target is not null)
            {
                AppendSelfAliases(code, members);
            }
            code.AppendLine("int count = 0;");
            code.AppendLine("if (" + KeyedItems(member) + " is not null)");
            code.OpenBlock();
            code.AppendLine("foreach (var item in " + KeyedItems(member) + ")");
            code.OpenBlock();
            code.AppendLine("if (item.IsRemoved) count++;");
            code.CloseBlock();
            code.CloseBlock();
            code.AppendLine("return count;");
            code.CloseBlock();
        }
    }

    internal static void AppendInvert(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseOperationTarget? target = null
    )
    {
        _ = runtime;
        _ = optionalFragment;
        var into = target?.ChangeSetOperations ?? code;
        // Per-key item inverters: reverse add/remove, nested edits,
        // endpoints, and indexes without recovering full snapshots.
        foreach (var member in members)
        {
            if (!IsKeyed(member) && !IsDict(member))
            {
                continue;
            }

            AppendItemInverter(into, members, member, runtime);
        }
        if (target is not null)
        {
            code.AppendLine("/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>");
            code.AppendLine("/// <returns>The inverted change set.</returns>");
            code.AppendLine("public ChangeSet Invert() => " + target.ChangeSetOperationsType + ".Invert(this);");
            code = into;
            code.AppendLine("/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>");
            code.AppendLine("/// <param name=\"self\">The change set to invert.</param>");
            code.AppendLine("/// <returns>The inverted change set.</returns>");
            code.AppendLine("internal static ChangeSet Invert(ChangeSet self)");
        }
        else
        {
            code.AppendLine("/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>");
            code.AppendLine("/// <returns>The inverted change set.</returns>");
            code.AppendLine("public ChangeSet Invert()");
        }
        code.OpenBlock();
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        var emptyTail = MemberEmptyTail(members);
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        if (string.IsNullOrEmpty(emptyTail))
        {
            code.AppendLine("return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore);");
        }
        else
        {
            code.AppendLine("return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore, " + emptyTail + ");");
        }

        code.CloseBlock();
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                args.Add(NestedField(member) + " is null ? null : " + NestedField(member) + ".Invert()");
            }
            else if (IsKeyed(member))
            {
                AddKeyedChangeSetArgs(
                    args,
                    member,
                    HasField(member),
                    KeyedWholeFlag(member),
                    KeyedWholeAfter(member),
                    KeyedWholeBefore(member),
                    "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                    KeyedAfterOrder(member),
                    KeyedBeforeOrder(member),
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? KeyedTempAfterOrder(member) : null,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? KeyedTempBeforeOrder(member) : null
                );
            }
            else if (IsDict(member))
            {
                args.AddRange(
                    new[]
                    {
                        HasField(member),
                        KeyedWholeFlag(member),
                        KeyedWholeAfter(member),
                        KeyedWholeBefore(member),
                        "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                    }
                );
            }
            else
            {
                args.AddRange(new[] { AfterField(member), BeforeField(member), HasField(member) });
            }
        }
        code.AppendLine("return new ChangeSet(" + string.Join(", ", args) + ");");
        code.CloseBlock();
    }

    internal static void AppendApplyToBaseline(SharedIndentedBuilder code, string optionalFragment, SparseOperationTarget? target = null)
    {
        // Baseline advancement for synchronous edit sessions.
        if (target is not null)
        {
            code.AppendLine("/// <summary>Validates this transition against the supplied baseline and returns the advanced baseline.</summary>");
            code.AppendLine("/// <remarks>Only changed paths are compared; members this change set does not encode are never validated.</remarks>");
            code.AppendLine(
                "/// <exception cref=\"global::System.InvalidOperationException\">Thrown when the transition is stale or incompatible with the baseline, or when advancing would not produce a valid model state.</exception>"
            );
            code.AppendLine("/// <param name=\"baseline\">The baseline to advance.</param>");
            code.AppendLine("/// <returns>The advanced baseline.</returns>");
            code.AppendLine(
                "public "
                    + optionalFragment
                    + " ApplyToBaseline("
                    + optionalFragment
                    + " baseline) => "
                    + target.ChangeSetOperationsType
                    + ".ApplyToBaseline(this, baseline);"
            );
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Validates a transition against the supplied baseline and returns the advanced baseline.</summary>");
            code.AppendLine("/// <remarks>Only changed paths are compared; members the change set does not encode are never validated.</remarks>");
            code.AppendLine(
                "/// <exception cref=\"global::System.InvalidOperationException\">Thrown when the transition is stale or incompatible with the baseline, or when advancing would not produce a valid model state.</exception>"
            );
            code.AppendLine("/// <param name=\"self\">The change set to apply.</param>");
            code.AppendLine("/// <param name=\"baseline\">The baseline to advance.</param>");
            code.AppendLine("/// <returns>The advanced baseline.</returns>");
            code.AppendLine("internal static " + optionalFragment + " ApplyToBaseline(ChangeSet self, " + optionalFragment + " baseline)");
        }
        else
        {
            code.AppendLine("/// <summary>Validates this transition against the supplied baseline and returns the advanced baseline.</summary>");
            code.AppendLine("/// <remarks>Only changed paths are compared; members this change set does not encode are never validated.</remarks>");
            code.AppendLine(
                "/// <exception cref=\"global::System.InvalidOperationException\">Thrown when the transition is stale or incompatible with the baseline, or when advancing would not produce a valid model state.</exception>"
            );
            code.AppendLine("/// <param name=\"baseline\">The baseline to advance.</param>");
            code.AppendLine("/// <returns>The advanced baseline.</returns>");
            code.AppendLine("public " + optionalFragment + " ApplyToBaseline(" + optionalFragment + " baseline)");
        }
        code.OpenBlock();
        code.AppendLine(
            target is null
                ? "if (!__SparseBeforeMatches(baseline)) throw new global::System.InvalidOperationException(\"The change set is stale or incompatible with the supplied baseline.\");"
                : "if (!self.__SparseBeforeMatches(baseline)) throw new global::System.InvalidOperationException(\"The change set is stale or incompatible with the supplied baseline.\");"
        );
        code.AppendLine(target is null ? "var __advanced = ToPatch().Apply(baseline);" : "var __advanced = ToPatch(self).Apply(baseline);");
        code.AppendLine(
            "if (!__advanced.IsPresent || __advanced.Value is null) throw new global::System.InvalidOperationException(\"Advancing the baseline did not produce a valid model state.\");"
        );
        code.AppendLine("return __advanced;");
        code.CloseBlock();
    }

    /// <summary>Emits the per-key item inverter for a sparse keyed/dict member.</summary>
    internal static void AppendItemInverter(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime
    )
    {
        _ = runtime;
        var trans = TransNameFor(members, member);
        var id = member.Id;
        code.AppendLine(
            "private static global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __SparseInvertItems_"
                + id
                + "(global::System.Collections.Generic.List<"
                + trans
                + ".Item>? items)"
        );
        code.OpenBlock();
        code.AppendLine("if (items is null) return null;");
        code.AppendLine("var __out = new global::System.Collections.Generic.List<" + trans + ".Item>(items.Count);");
        code.AppendLine("foreach (var __it in items)");
        code.OpenBlock();
        if (IsKeyed(member))
        {
            code.AppendLine(
                "__out.Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, __it.After, __it.Before, __it.AfterIndex, __it.BeforeIndex, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.IsReordered, __it.Edit.Invert(), false));"
            );
        }
        else
        {
            var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
            if (hasPatch)
            {
                code.AppendLine(
                    "__out.Add(new "
                        + trans
                        + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.Edit.Invert(), false));"
                );
            }
            else
            {
                code.AppendLine(
                    "__out.Add(new " + trans + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, false));"
                );
            }
        }
        code.CloseBlock();
        code.AppendLine("return __out;");
        code.CloseBlock();
    }

    /// <summary>Emits ToPatch projection for a sparse keyed/dict member.</summary>
    internal static void AppendKeyedDictToPatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? removalCountExpression = null,
        string? selfName = null
    )
    {
        var id = member.Id;
        var __facade = dialect.RuntimeFacade;
        var coll = "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        var isKeyed = IsKeyed(member);
        var hasValuePatch = isKeyed ? member.Collection.ElementType.IsFragmentModel : member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLine("if (" + HasField(member) + ")");
        code.OpenBlock();
        // Whole presence/null transitions project via canonical collection
        // Between.
        code.AppendLine("if (" + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine("patch." + escName + " = " + coll + ".Between(" + KeyedWholeBefore(member) + ", " + KeyedWholeAfter(member) + ");");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var __coll" + id + " = new " + coll + "();");
        if (isKeyed)
        {
            code.AppendLine(
                "var __conversionAddedKeys"
                    + id
                    + " = new global::System.Collections.Generic.HashSet<"
                    + SparseChangeSetBasicsEmitter.KeyTypeOf(member)
                    + ">(global::System.Collections.Generic.EqualityComparer<"
                    + SparseChangeSetBasicsEmitter.KeyTypeOf(member)
                    + ">.Default);"
            );
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "var __conversionAddedTemporaryKeys" + id + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
                );
                code.AppendLine(
                    "var __conversionRemovedTemporaryKeys" + id + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
                );
            }
        }
        if (!isKeyed)
        {
            code.AppendLine("bool __reservedRemovals" + id + " = false;");
        }
        code.AppendLine("if (" + KeyedItems(member) + " is not null) foreach (var __it in " + KeyedItems(member) + ")");
        code.OpenBlock();
        if (isKeyed)
        {
            code.AppendLine(
                "if (__it.IsAdded) __coll"
                    + id
                    + ".__SparseAddForChangeSet(__it.After.Value!, __conversionAddedKeys"
                    + id
                    + (SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? ", __conversionAddedTemporaryKeys" + id : "")
                    + ");"
            );
        }
        else
        {
            code.AppendLine("if (__it.IsAdded) __coll" + id + ".SetEntry(__it.Key!, __it.After.Value!);");
        }

        code.AppendLine("else if (__it.IsRemoved)");
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary removals address by Guid; assigned removals keep key
            // addressing.
            code.AppendLine(
                "{ if (__it.TemporaryKey.HasValue) __coll"
                    + id
                    + ".__SparseRemoveByTemporaryKeyForChangeSet(__it.TemporaryKey.Value, __conversionAddedTemporaryKeys"
                    + id
                    + ", __conversionRemovedTemporaryKeys"
                    + id
                    + "); else __coll"
                    + id
                    + ".__SparseRemoveForChangeSet(__it.Key, __conversionAddedKeys"
                    + id
                    + "); }"
            );
        }
        else if (isKeyed)
        {
            code.AppendLine("{ __coll" + id + ".__SparseRemoveForChangeSet(__it.Key, __conversionAddedKeys" + id + "); }");
        }
        else
        {
            code.OpenBlock();
            code.AppendLine("if (!__reservedRemovals" + id + ")");
            code.OpenBlock();
            code.AppendLine(
                "__coll"
                    + id
                    + ".__SparseReserveRemovals("
                    + (removalCountExpression ?? "__SparseRemovalCount" + id + "(" + (selfName ?? string.Empty) + ")")
                    + ");"
            );
            code.AppendLine("__reservedRemovals" + id + " = true;");
            code.CloseBlock();
            code.AppendLine("__coll" + id + ".RemoveEntry(__it.Key!);");
            code.CloseBlock();
        }
        code.AppendLine("else if (__it.IsEdited)");
        code.OpenBlock();
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary edits address by Guid; assigned edits keep key
            // addressing.
            code.AppendLine("var __isTempEdit = __it.TemporaryKey.HasValue;");
        }
        if (hasValuePatch)
        {
            if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "if (__isTempEdit) __coll"
                        + id
                        + ".__SparseSetEditedByTemporaryKey(__it.TemporaryKey!.Value, __it.Edit.ToPatch()); else __coll"
                        + id
                        + ".__SparseSetEdited(__it.Key, __it.Edit.ToPatch());"
                );
            }
            else
            {
                code.AppendLine("__coll" + id + ".__SparseSetEdited(__it.Key, __it.Edit.ToPatch());");
            }
        }
        else if (isKeyed)
        {
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "if (__isTempEdit) __coll"
                        + id
                        + ".__SparseUpdateByTemporaryKeyForChangeSet(__it.After.Value!, __conversionAddedTemporaryKeys"
                        + id
                        + ", __conversionRemovedTemporaryKeys"
                        + id
                        + "); else __coll"
                        + id
                        + ".__SparseUpdateForChangeSet(__it.After.Value!, __conversionAddedKeys"
                        + id
                        + ", __conversionAddedTemporaryKeys"
                        + id
                        + ", __conversionRemovedTemporaryKeys"
                        + id
                        + ");"
                );
            }
            else
            {
                code.AppendLine("__coll" + id + ".__SparseUpdateForChangeSet(__it.After.Value!, __conversionAddedKeys" + id + ");");
            }
        }
        else
        {
            code.AppendLine("__coll" + id + ".UpdateEntry(__it.Key!, __it.After.Value!);");
        }
        code.CloseBlock();
        // Reorder-only items contribute no add/remove/edit; order is applied
        // below.
        code.CloseBlock();
        if (isKeyed)
        {
            var beforeOrder = KeyedBeforeOrder(member);
            var afterOrder = KeyedAfterOrder(member);
            var tempBeforeOrder = KeyedTempBeforeOrder(member);
            var tempAfterOrder = KeyedTempAfterOrder(member);
            var orderChanged = KeyedOrderChangedExpression(member, __facade);
            // Equal sentinel-key sequences can still carry a temporary-identity
            // reorder.
            code.AppendLine($"if ({beforeOrder} is not null && {afterOrder} is not null && ({orderChanged}))");
            code.OpenBlock();
            code.AppendLine($"__coll{id}.SetOrder({afterOrder});");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine($"if ({tempBeforeOrder} is not null && {tempAfterOrder} is not null)");
                code.OpenBlock();
                code.AppendLine($"__coll{id}.__orderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();");
                code.AppendLine(
                    $"{{ var __ti = 0; foreach (var __ok in {afterOrder}) {{ if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok")
                        + $") __coll{id}.__orderTemp.Add(__ti < {tempAfterOrder}.Count ? (global::System.Guid?){tempAfterOrder}[__ti++] : null); else __coll{id}.__orderTemp.Add(null); }} }}"
                );
                code.CloseBlock();
            }
            code.CloseBlock();
        }
        code.AppendLine("patch." + escName + " = __coll" + id + ";");
        code.CloseBlock();
        code.CloseBlock();
    }
}
