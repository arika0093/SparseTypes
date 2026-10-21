namespace SparseTypes.Generator.Shared;

/// <summary>Emits keyed-sequence Compose algebra.</summary>
/// <remarks>Delegates key-set composition to shared kernel helpers.</remarks>
internal static class SparseKeyedSequenceComposeEmitter
{
    internal static void EmitKeyedCompose(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string runtime,
        SparseMemberOperationSplit? split = null
    )
    {
        var kind = runtime + "FragmentOperationKind";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        if (split is not null)
        {
            split.Shell.AppendLine(
                "/// <summary>Composes this keyed patch with a following patch.</summary>"
            );
            split.Shell.AppendLine(
                "public "
                    + patchName
                    + " Compose("
                    + patchName
                    + " next) => "
                    + split.OperationsType
                    + ".Compose(this, next);"
            );
            split.Shell.AppendLine("/// <summary>Composes two keyed patches.</summary>");
            split.Shell.AppendLine(
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) => "
                    + split.OperationsType
                    + ".Compose(first, second);"
            );
            code.AppendLine(
                "/// <summary>Composes a keyed patch with a following patch.</summary>"
            );
            code.AppendLine(
                "internal static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " self, "
                    + patchName
                    + " next)"
            );
        }
        else
        {
            code.AppendLine("public " + patchName + " Compose(" + patchName + " next)");
        }
        code.OpenBlock();
        if (split is not null)
        {
            SparseKeyedSequenceApplyEmitter.AppendKeyedFieldAliases(code, member);
        }
        code.AppendLine(
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLine("var result = new " + patchName + "();");
        code.AppendLine("if (next.__whole.Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine("result.__whole = next.__whole;");
        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine("if (__whole.Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine(
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLine(
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + listType
                + ">.Present(__whole.Value));"
        );
        code.AppendLine("result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine(
            "if (" + (split is null ? "this" : "self") + ".IsEmpty && next.IsEmpty) return result;"
        );
        // Granular + granular: merge key operation logs preserving sequential
        // semantics. Added keys.
        var hasTempPatchCompose = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (!hasTempPatchCompose && !SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedAddThenRemoveCancellationFastPath(
                code,
                comparer,
                member
            );
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedRemoveThenAddFastPath(
                code,
                keyType,
                elementType
            );
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedOrderOnlyComposeFastPath(
                code,
                keyType
            );
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedEditThenRemoveFastPath(
                code,
                keyType,
                comparer
            );
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedAddOnlyComposeFastPath(
                code,
                keyType,
                elementType,
                comparer,
                member
            );
            SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedRemoveOnlyComposeFastPath(
                code,
                keyType,
                comparer
            );
            if (hasPatch)
            {
                SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedAddedThenEditedComposeFastPath(
                    code,
                    keyType,
                    elementType,
                    comparer,
                    member,
                    runtime
                );
                SparseKeyedSequenceComposeFastPathEmitter.AppendAssignedEditOnlyComposeFastPath(
                    code,
                    keyType,
                    comparer,
                    member
                );
            }
        }

        if (hasTempPatchCompose)
        {
            SparseKeyedSequenceTempPatchMergeEmitter.AppendThisTempPartition(
                code,
                member,
                elementType,
                member.Id
            );
            SparseKeyedSequenceTempPatchMergeEmitter.AppendNextTempPartition(
                code,
                member,
                elementType,
                member.Id
            );
        }
        code.AppendLine(
            "var thisAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "var thisUnassignedAdded = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            // Temporary adds merge by Guid below; legacy temp-less adds keep
            // positional flow.
            var partitionFilter = hasTempPatchCompose
                ? "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { if (!"
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(item).HasValue) thisUnassignedAdded.Add(item); }"
                    + " else thisAdded[k] = item; }"
                : "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") thisUnassignedAdded.Add(item); else thisAdded[k] = item; }";
            code.AppendLine(
                "if (__added is not null) foreach (var item in __added) { var k = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); "
                    + partitionFilter
            );
        }
        else
        {
            code.AppendLine(
                "if (__added is not null) foreach (var item in __added) thisAdded["
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item)] = item;"
            );
        }
        code.AppendLine(
            "var nextAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "var nextUnassignedAdded = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (next.__added is not null) foreach (var item in next.__added) { var k = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") nextUnassignedAdded.Add(item); else nextAdded[k] = item; }"
            );
        }
        else
        {
            code.AppendLine(
                "if (next.__added is not null) foreach (var item in next.__added) nextAdded["
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item)] = item;"
            );
        }
        code.AppendLine(
            "var thisRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        code.AppendLine(
            "var nextRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(next.__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        // Net removals: removed by either.
        code.AppendLine(
            "var netRemoved = new global::System.Collections.Generic.List<"
                + keyType
                + ">(thisRemoved.Count + nextRemoved.Count);"
        );
        code.AppendLine(
            "var __netRemovedSet = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "foreach (var k in thisRemoved) if (!nextAdded.ContainsKey(k) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        code.AppendLine(
            "foreach (var k in nextRemoved) if ((!thisAdded.ContainsKey(k) || thisRemoved.Contains(k)) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        // Net adds: this-added surviving next-removal (with next edits
        // applied), plus next-added.
        code.AppendLine(
            "var netAdded = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLine("foreach (var kv in thisAdded)");
            code.OpenBlock();
            code.AppendLine("if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLine(
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var nextEdit))"
            );
            code.OpenBlock();
            code.AppendLine(
                "var applied = nextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLine(
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLine(
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(applied.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLine("netAdded.Add(applied.Value!.ToModel());");
            code.CloseBlock();
            code.AppendLine("else netAdded.Add(kv.Value);");
            code.CloseBlock();
            code.AppendLine(
                "foreach (var item in thisUnassignedAdded) netAdded.Add(item); if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLine(
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">? netEdited = null;"
            );
            code.AppendLine("if (__edited is not null) foreach (var kv in __edited)");
            code.OpenBlock();
            code.AppendLine("if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLine("if (nextAdded.ContainsKey(kv.Key)) continue;");
            code.AppendLine(
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var n2) && !n2.__SparseIsEmpty()) { var __c = kv.Value.Compose(n2); if (__c.__SparseIsEmpty()) { netEdited?.Remove(kv.Key); } else (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = __c; }"
            );
            code.AppendLine(
                "else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty()) (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.CloseBlock();
            code.AppendLine("if (next.__edited is not null) foreach (var kv in next.__edited)");
            code.OpenBlock();
            code.AppendLine(
                "if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue;"
            );
            code.AppendLine("if (kv.Value.__SparseIsEmpty()) continue;");
            code.AppendLine(
                "if (netEdited is not null && netEdited.ContainsKey(kv.Key)) continue;"
            );
            code.AppendLine(
                "(netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.CloseBlock();
            if (hasTempPatchCompose)
            {
                SparseKeyedSequenceTempPatchMergeEmitter.AppendPatchTempMerge(
                    code,
                    member,
                    elementType,
                    hasPatch,
                    runtime
                );
            }
            code.AppendLine("if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLine("if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLine(
                "if (netEdited is not null && netEdited.Count > 0) result.__edited = netEdited;"
            );
        }
        else
        {
            code.AppendLine("foreach (var kv in thisAdded)");
            code.OpenBlock();
            code.AppendLine("if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLine(
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var upd)) netAdded.Add(upd); else netAdded.Add(kv.Value);"
            );
            code.CloseBlock();
            code.AppendLine(
                "foreach (var item in thisUnassignedAdded) netAdded.Add(item); if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLine(
                "var netEdited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLine(
                "if (__edited is not null) foreach (var kv in __edited) if (!nextRemoved.Contains(kv.Key) && !nextAdded.ContainsKey(kv.Key)) netEdited[kv.Key] = kv.Value;"
            );
            code.AppendLine(
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue; netEdited[kv.Key] = kv.Value; }"
            );
            if (hasTempPatchCompose)
            {
                SparseKeyedSequenceTempPatchMergeEmitter.AppendPatchTempMerge(
                    code,
                    member,
                    elementType,
                    hasPatch,
                    runtime
                );
            }
            code.AppendLine("if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLine("if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLine("if (netEdited.Count > 0) result.__edited = netEdited;");
        }

        // Order: next order wins when present (filtered to net keys), else this
        // order filtered.
        if (hasTempPatchCompose)
        {
            SparseKeyedSequenceTempPatchMergeEmitter.AppendPatchOrderTempMerge(
                code,
                member,
                keyType,
                comparer,
                member.Id
            );
        }
        else
        {
            code.AppendLine("if (next.__order is not null && next.__order.Count > 0)");
            code.OpenBlock();
            // The removal set and the order-membership set both use the
            // configured key comparer, which is EqualityComparer<T>.Default at
            // every construction site today; List.Contains and
            // Enumerable.Contains over the same comparer therefore agree with
            // set membership, and the sets only replace linear scans. The
            // filtered list keeps every surviving entry (duplicates included);
            // the set only gates later additions.
            code.AppendLine(
                "var __nextOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLine(
                "var __nextOrderSet = new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLine(
                "foreach (var k in next.__order) if (!__netRemovedSet.Contains(k)) { __nextOrder.Add(k); __nextOrderSet.Add(k); }"
            );
            code.AppendLine(
                "if (next.__added is not null) foreach (var item in next.__added) { var __ak = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); if (__nextOrderSet.Add(__ak)) __nextOrder.Add(__ak); }"
            );
            code.AppendLine("if (__nextOrder.Count > 0) result.__order = __nextOrder;");
            code.CloseBlock();
            code.AppendLine("else if (__order is not null && __order.Count > 0)");
            code.OpenBlock();
            code.AppendLine(
                "var order = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLine(
                "foreach (var k in __order) if (!nextRemoved.Contains(k)) order.Add(k);"
            );
            code.AppendLine(
                "if (next.__added is not null) foreach (var item in next.__added) order.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLine("if (order.Count > 0) result.__order = order;");
            code.CloseBlock();
        }
        code.AppendLine("return result;");
        code.CloseBlock();
        if (split is null)
        {
            code.AppendLine(
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) { if (first is null) throw new global::System.ArgumentNullException(nameof(first)); return first.Compose(second); }"
            );
        }
    }
}
