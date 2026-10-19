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
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Composes this keyed patch with a following patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Compose("
                    + patchName
                    + " next) => "
                    + split.OperationsType
                    + ".Compose(this, next);"
            );
            split.Shell.AppendLineAt(3, "/// <summary>Composes two keyed patches.</summary>");
            split.Shell.AppendLineAt(
                3,
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
            code.AppendLineAt(
                3,
                "/// <summary>Composes a keyed patch with a following patch.</summary>"
            );
            code.AppendLineAt(
                3,
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
            code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
        }
        code.AppendLineAt(3, "{");
        if (split is not null)
        {
            SparseKeyedSequenceApplyEmitter.AppendKeyedFieldAliases(code, member);
        }
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(4, "if (next.__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "result.__whole = next.__whole;");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLineAt(
            5,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + listType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(5, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (" + (split is null ? "this" : "self") + ".IsEmpty && next.IsEmpty) return result;"
        );
        // Granular + granular: merge key operation logs preserving sequential
        // semantics. Added keys.
        var hasTempPatchCompose = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (!hasTempPatchCompose && !SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            AppendAssignedAddOnlyComposeFastPath(code, keyType, elementType, comparer, member);
            AppendAssignedRemoveOnlyComposeFastPath(code, keyType, comparer);
            if (hasPatch)
            {
                AppendAssignedEditOnlyComposeFastPath(code, keyType, comparer, member);
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
        code.AppendLineAt(
            4,
            "var thisAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
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
            code.AppendLineAt(
                4,
                "if (__added is not null) foreach (var item in __added) { var k = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); "
                    + partitionFilter
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "if (__added is not null) foreach (var item in __added) thisAdded["
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item)] = item;"
            );
        }
        code.AppendLineAt(
            4,
            "var nextAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var nextUnassignedAdded = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) { var k = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") nextUnassignedAdded.Add(item); else nextAdded[k] = item; }"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) nextAdded["
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item)] = item;"
            );
        }
        code.AppendLineAt(
            4,
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
        code.AppendLineAt(
            4,
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
        code.AppendLineAt(
            4,
            "var netRemoved = new global::System.Collections.Generic.List<"
                + keyType
                + ">(thisRemoved.Count + nextRemoved.Count);"
        );
        code.AppendLineAt(
            4,
            "var __netRemovedSet = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in thisRemoved) if (!nextAdded.ContainsKey(k) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in nextRemoved) if ((!thisAdded.ContainsKey(k) || thisRemoved.Contains(k)) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        // Net adds: this-added surviving next-removal (with next edits
        // applied), plus next-added.
        code.AppendLineAt(
            4,
            "var netAdded = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var nextEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var applied = nextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLineAt(
                6,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(
                6,
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(applied.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(6, "netAdded.Add(applied.Value!.ToModel());");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else netAdded.Add(kv.Value);");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "foreach (var item in thisUnassignedAdded) netAdded.Add(item); if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">? netEdited = null;"
            );
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(5, "if (nextAdded.ContainsKey(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var n2) && !n2.__SparseIsEmpty()) { var __c = kv.Value.Compose(n2); if (__c.__SparseIsEmpty()) { netEdited?.Remove(kv.Key); } else (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = __c; }"
            );
            code.AppendLineAt(
                5,
                "else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty()) (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited)"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue;"
            );
            code.AppendLineAt(5, "if (kv.Value.__SparseIsEmpty()) continue;");
            code.AppendLineAt(
                5,
                "if (netEdited is not null && netEdited.ContainsKey(kv.Key)) continue;"
            );
            code.AppendLineAt(
                5,
                "(netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
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
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(
                4,
                "if (netEdited is not null && netEdited.Count > 0) result.__edited = netEdited;"
            );
        }
        else
        {
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var upd)) netAdded.Add(upd); else netAdded.Add(kv.Value);"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "foreach (var item in thisUnassignedAdded) netAdded.Add(item); if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "var netEdited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) if (!nextRemoved.Contains(kv.Key) && !nextAdded.ContainsKey(kv.Key)) netEdited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(
                4,
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
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(4, "if (netEdited.Count > 0) result.__edited = netEdited;");
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
            code.AppendLineAt(4, "if (next.__order is not null && next.__order.Count > 0)");
            code.AppendLineAt(4, "{");
            // The removal set and the order-membership set both use the
            // configured key comparer, which is EqualityComparer<T>.Default at
            // every construction site today; List.Contains and
            // Enumerable.Contains over the same comparer therefore agree with
            // set membership, and the sets only replace linear scans. The
            // filtered list keeps every surviving entry (duplicates included);
            // the set only gates later additions.
            code.AppendLineAt(
                5,
                "var __nextOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                5,
                "var __nextOrderSet = new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "foreach (var k in next.__order) if (!__netRemovedSet.Contains(k)) { __nextOrder.Add(k); __nextOrderSet.Add(k); }"
            );
            code.AppendLineAt(
                5,
                "if (next.__added is not null) foreach (var item in next.__added) { var __ak = "
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item); if (__nextOrderSet.Add(__ak)) __nextOrder.Add(__ak); }"
            );
            code.AppendLineAt(5, "if (__nextOrder.Count > 0) result.__order = __nextOrder;");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else if (__order is not null && __order.Count > 0)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var order = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                5,
                "foreach (var k in __order) if (!nextRemoved.Contains(k)) order.Add(k);"
            );
            code.AppendLineAt(
                5,
                "if (next.__added is not null) foreach (var item in next.__added) order.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(5, "if (order.Count > 0) result.__order = order;");
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        if (split is null)
        {
            code.AppendLineAt(
                3,
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

    private static void AppendAssignedAddOnlyComposeFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string elementType,
        string comparer,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">? __simpleAdded = null;"
        );
        code.AppendLineAt(5, "if (__added is not null && __added.Count > 0)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "__simpleAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__added.Count, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var item in __added) __simpleAdded["
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "var __simpleAddedCount = (__simpleAdded?.Count ?? 0) + (next.__added?.Count ?? 0);"
        );
        code.AppendLineAt(
            5,
            "if (__simpleAddedCount > 0) result.__added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(__simpleAddedCount);"
        );
        code.AppendLineAt(
            5,
            "if (__simpleAdded is not null) foreach (var item in __simpleAdded.Values) result.__added!.Add(item);"
        );
        code.AppendLineAt(
            5,
            "if (next.__added is not null) result.__added!.AddRange(next.__added);"
        );
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
    }

    private static void AppendAssignedRemoveOnlyComposeFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string comparer
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__added is null || __added.Count == 0)");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (next.__removed is not null && next.__removed.Count > 0)");
        code.AppendLineAt(
            7,
            "result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(next.__removed);"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(
            6,
            "result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__removed);"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __simpleRemovedSeen = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "var __simpleRemoved = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__removed.Count + next.__removed.Count);"
        );
        code.AppendLineAt(
            6,
            "foreach (var key in __removed) if (__simpleRemovedSeen.Add(key)) __simpleRemoved.Add(key);"
        );
        code.AppendLineAt(
            6,
            "foreach (var key in next.__removed) if (__simpleRemovedSeen.Add(key)) __simpleRemoved.Add(key);"
        );
        code.AppendLineAt(6, "result.__removed = __simpleRemoved;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
    }

    private static void AppendAssignedEditOnlyComposeFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string comparer,
        SparseMemberModel member
    )
    {
        var elementPatchType = SparseKeyedCollectionEmitter.ElementPatchType(member);
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__added is null || __added.Count == 0)");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">? __simpleEdited = null;"
        );
        code.AppendLineAt(5, "if (__edited is not null) foreach (var kv in __edited)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var __nextEdit) && !__nextEdit.__SparseIsEmpty())"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __composedEdit = kv.Value.Compose(__nextEdit);");
        code.AppendLineAt(
            7,
            "if (__composedEdit.__SparseIsEmpty()) __simpleEdited?.Remove(kv.Key);"
        );
        code.AppendLineAt(
            7,
            "else (__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = __composedEdit;"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty())"
        );
        code.AppendLineAt(
            7,
            "(__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (next.__edited is not null) foreach (var kv in next.__edited)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (kv.Value.__SparseIsEmpty()) continue;");
        code.AppendLineAt(
            6,
            "if (__simpleEdited is not null && __simpleEdited.ContainsKey(kv.Key)) continue;"
        );
        code.AppendLineAt(
            6,
            "(__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__simpleEdited is not null && __simpleEdited.Count > 0) result.__edited = __simpleEdited;"
        );
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
    }
}
