namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity operation merge for keyed-sequence patches.</summary>
/// <remarks>Merges Guid-keyed removal/edit logs and routes temporary adds through cancellation and edit-application.</remarks>
internal static class SparseKeyedSequenceTempPatchMergeEmitter
{
    /// <summary>Emits the full temporary merge for patch Compose.</summary>
    internal static void AppendPatchTempMerge(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool hasPatch,
        string runtime
    )
    {
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ElementPatchType(member)
            : elementType;
        AppendTempRemovalMerge(code, member.Id);
        AppendThisTempAdds(code, member, hasPatch, runtime, member.Id);
        AppendTempEditMerge(code, editedValueType, hasPatch, member.Id);
    }

    /// <summary>Emits temporary partition of this-side adds for patch Compose.</summary>
    internal static void AppendThisTempPartition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLine(
            "var thisTempAdded"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLine(
            "var thisTempAddedOrder"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLine(
            "if (__added is not null) foreach (var item in __added) { var __tk = "
                + tempOf
                + "(item); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)"
                )
                + " && __tk.HasValue && __tk.Value != global::System.Guid.Empty) { if (thisTempAdded"
                + id
                + ".ContainsKey(__tk.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); thisTempAdded"
                + id
                + ".Add(__tk.Value, item); thisTempAddedOrder"
                + id
                + ".Add(__tk.Value); } }"
        );
    }

    /// <summary>Emits temporary partition of next-side adds for patch Compose.</summary>
    internal static void AppendNextTempPartition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLine(
            "var nextTempAdded"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLine(
            "if (next.__added is not null) foreach (var item in next.__added) { var __tk = "
                + tempOf
                + "(item); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)"
                )
                + " && __tk.HasValue && __tk.Value != global::System.Guid.Empty) { if (nextTempAdded"
                + id
                + ".ContainsKey(__tk.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); nextTempAdded"
                + id
                + ".Add(__tk.Value, item); } }"
        );
    }

    /// <summary>Emits temporary removal merge for patch Compose.</summary>
    internal static void AppendTempRemovalMerge(SharedIndentedBuilder code, int id)
    {
        // Net temporary removals: removed by either side, minus re-added later.
        // The ordered list stays authoritative; the companion set only replaces
        // repeated linear deduplication scans. Guid equality is the default in
        // both representations, so membership semantics are unchanged.
        code.AppendLine(
            "global::System.Collections.Generic.HashSet<global::System.Guid>? __firstRemovedTempSet"
                + id
                + " = (__removedTemp is null || __removedTemp.Count == 0) ? null : new global::System.Collections.Generic.HashSet<global::System.Guid>(__removedTemp);"
        );
        code.AppendLine(
            "global::System.Collections.Generic.HashSet<global::System.Guid>? __nextRemovedTempSet"
                + id
                + " = (next.__removedTemp is null || next.__removedTemp.Count == 0) ? null : new global::System.Collections.Generic.HashSet<global::System.Guid>(next.__removedTemp);"
        );
        code.AppendLine(
            "var __netRemovedTempSet"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLine(
            "var netRemovedTemp"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLine(
            "if (__removedTemp is not null) foreach (var __t in __removedTemp) if (!nextTempAdded"
                + id
                + ".ContainsKey(__t) && __netRemovedTempSet"
                + id
                + ".Add(__t)) netRemovedTemp"
                + id
                + ".Add(__t);"
        );
        code.AppendLine(
            "if (next.__removedTemp is not null) foreach (var __t in next.__removedTemp) if ((!thisTempAdded"
                + id
                + ".ContainsKey(__t) || (__firstRemovedTempSet"
                + id
                + " is not null && __firstRemovedTempSet"
                + id
                + ".Contains(__t))) && __netRemovedTempSet"
                + id
                + ".Add(__t)) netRemovedTemp"
                + id
                + ".Add(__t);"
        );
        code.AppendLine(
            "if (netRemovedTemp"
                + id
                + ".Count > 0) result.__removedTemp = netRemovedTemp"
                + id
                + ";"
        );
    }

    /// <summary>Emits this-side temporary adds with next-side cancellation and edits.</summary>
    internal static void AppendThisTempAdds(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        bool hasPatch,
        string runtime,
        int id
    )
    {
        code.AppendLine("foreach (var __tt in thisTempAddedOrder" + id + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (__nextRemovedTempSet"
                + id
                + " is not null && __nextRemovedTempSet"
                + id
                + ".Contains(__tt)) continue;"
        );
        code.AppendLine("var __titem = thisTempAdded" + id + "[__tt];");
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLine(
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tt, out var __tnextEdit))"
            );
            code.OpenBlock();
            code.AppendLine(
                "var __tapplied = __tnextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__titem)));"
            );
            code.AppendLine(
                "if (!__tapplied.IsPresent || __tapplied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLine("netAdded.Add(__tapplied.Value!.ToModel());");
            code.CloseBlock();
            code.AppendLine("else netAdded.Add(__titem);");
        }
        else
        {
            code.AppendLine(
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tt, out var __tupd)) netAdded.Add(__tupd); else netAdded.Add(__titem);"
            );
        }
        code.CloseBlock();
    }

    /// <summary>Emits temporary edit merge for patch Compose.</summary>
    internal static void AppendTempEditMerge(
        SharedIndentedBuilder code,
        string editedValueType,
        bool hasPatch,
        int id
    )
    {
        code.AppendLine(
            "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + editedValueType
                + ">? netEditedTemp"
                + id
                + " = null;"
        );
        code.AppendLine("if (__editedTemp is not null) foreach (var __tkv in __editedTemp)");
        code.OpenBlock();
        code.AppendLine(
            "if (__nextRemovedTempSet"
                + id
                + " is not null && __nextRemovedTempSet"
                + id
                + ".Contains(__tkv.Key)) continue;"
        );
        code.AppendLine("if (nextTempAdded" + id + ".ContainsKey(__tkv.Key)) continue;");
        if (hasPatch)
        {
            code.AppendLine(
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tkv.Key, out var __tn2) && !__tn2.__SparseIsEmpty()) { var __tc = __tkv.Value.Compose(__tn2); if (__tc.__SparseIsEmpty()) { netEditedTemp"
                    + id
                    + "?.Remove(__tkv.Key); } else (netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">())[__tkv.Key] = __tc; }"
            );
            code.AppendLine(
                "else if ((next.__editedTemp is null || !next.__editedTemp.ContainsKey(__tkv.Key)) && !__tkv.Value.__SparseIsEmpty()) (netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">())[__tkv.Key] = __tkv.Value;"
            );
        }
        else
        {
            code.AppendLine(
                "(netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">());"
            );
            code.AppendLine(
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tkv.Key, out var __tnv)) netEditedTemp"
                    + id
                    + "[__tkv.Key] = __tnv; else netEditedTemp"
                    + id
                    + "[__tkv.Key] = __tkv.Value;"
            );
        }
        code.CloseBlock();
        code.AppendLine(
            "if (next.__editedTemp is not null) foreach (var __tkv in next.__editedTemp)"
        );
        code.OpenBlock();
        code.AppendLine("if (thisTempAdded" + id + ".ContainsKey(__tkv.Key)) continue;");
        if (hasPatch)
        {
            code.AppendLine("if (__tkv.Value.__SparseIsEmpty()) continue;");
        }
        code.AppendLine(
            "if (netEditedTemp"
                + id
                + " is not null && netEditedTemp"
                + id
                + ".ContainsKey(__tkv.Key)) continue;"
        );
        code.AppendLine(
            "(netEditedTemp"
                + id
                + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + editedValueType
                + ">())[__tkv.Key] = __tkv.Value;"
        );
        code.CloseBlock();
        code.AppendLine(
            "if (netEditedTemp"
                + id
                + " is not null && netEditedTemp"
                + id
                + ".Count > 0) result.__editedTemp = netEditedTemp"
                + id
                + ";"
        );
    }

    /// <summary>Emits temporary order lockstep for patch Compose.</summary>
    /// <remarks>Replaces the key-only order merge for temporary members.</remarks>
    internal static void AppendPatchOrderTempMerge(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string comparer,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var sentinel = member.Collection.UnassignedKeyExpression ?? "default!";
        // Assigned-key removal filtering reuses the composition's
        // __netRemovedSet (configured comparer, currently always
        // EqualityComparer<T>.Default, matching the previous List.Contains).
        // __assignedSeen only gates later additions; copied order entries keep
        // their exact list contents.
        code.AppendLine("if (next.__order is not null && next.__order.Count > 0)");
        code.OpenBlock();
        code.AppendLine(
            "var __nextOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLine(
            "var __nextOrderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();"
        );
        code.AppendLine(
            "var __slotted = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLine(
            "var __assignedSeen = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine("for (var __oi = 0; __oi < next.__order.Count; __oi++)");
        code.OpenBlock();
        code.AppendLine("var __ok = next.__order[__oi];");
        code.AppendLine(
            "global::System.Guid? __ot = (next.__orderTemp is not null && __oi < next.__orderTemp.Count) ? next.__orderTemp[__oi] : null;"
        );
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok")
                + ") { if (__ot.HasValue && __netRemovedTempSet"
                + id
                + ".Contains(__ot.Value)) continue; __nextOrder.Add(__ok); __nextOrderTemp.Add(__ot); if (__ot.HasValue) __slotted.Add(__ot.Value); }"
        );
        code.AppendLine(
            "else { if (__netRemovedSet.Contains(__ok)) continue; __nextOrder.Add(__ok); __nextOrderTemp.Add(null); __assignedSeen.Add(__ok); }"
        );
        code.CloseBlock();
        code.AppendLine("if (next.__added is not null) foreach (var __item in next.__added)");
        code.OpenBlock();
        code.AppendLine("var __ak = " + keyOf + "(__item);");
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ak")
                + ") { var __atk = "
                + tempOf
                + "(__item); if (__atk.HasValue && __atk.Value != global::System.Guid.Empty && !__slotted.Contains(__atk.Value)) { __nextOrder.Add("
                + sentinel
                + "); __nextOrderTemp.Add(__atk); __slotted.Add(__atk.Value); } }"
        );
        code.AppendLine(
            "else if (__assignedSeen.Add(__ak)) { __nextOrder.Add(__ak); __nextOrderTemp.Add(null); }"
        );
        code.CloseBlock();
        code.AppendLine(
            "foreach (var __tt in thisTempAddedOrder"
                + id
                + ") if (!__slotted.Contains(__tt)) { __nextOrder.Add("
                + sentinel
                + "); __nextOrderTemp.Add(__tt); __slotted.Add(__tt); }"
        );
        code.AppendLine(
            "if (__nextOrder.Count > 0) { result.__order = __nextOrder; result.__orderTemp = __nextOrderTemp; }"
        );
        code.CloseBlock();
        code.AppendLine("else if (__order is not null && __order.Count > 0)");
        code.OpenBlock();
        code.AppendLine(
            "var __thisOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLine(
            "var __thisOrderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();"
        );
        code.AppendLine(
            "var __slotted = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLine("for (var __oi = 0; __oi < __order.Count; __oi++)");
        code.OpenBlock();
        code.AppendLine("var __ok = __order[__oi];");
        code.AppendLine(
            "global::System.Guid? __ot = (__orderTemp is not null && __oi < __orderTemp.Count) ? __orderTemp[__oi] : null;"
        );
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok")
                + ") { if (__ot.HasValue && __netRemovedTempSet"
                + id
                + ".Contains(__ot.Value)) continue; __thisOrder.Add(__ok); __thisOrderTemp.Add(__ot); if (__ot.HasValue) __slotted.Add(__ot.Value); }"
        );
        code.AppendLine(
            "else { if (__netRemovedSet.Contains(__ok)) continue; __thisOrder.Add(__ok); __thisOrderTemp.Add(null); }"
        );
        code.CloseBlock();
        code.AppendLine("if (next.__added is not null) foreach (var __item in next.__added)");
        code.OpenBlock();
        code.AppendLine("var __ak = " + keyOf + "(__item);");
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ak")
                + ") { var __atk = "
                + tempOf
                + "(__item); if (__atk.HasValue && __atk.Value != global::System.Guid.Empty && !__slotted.Contains(__atk.Value)) { __thisOrder.Add("
                + sentinel
                + "); __thisOrderTemp.Add(__atk); __slotted.Add(__atk.Value); } else if (!__atk.HasValue) { __thisOrder.Add(__ak); __thisOrderTemp.Add(null); } }"
        );
        code.AppendLine("else { __thisOrder.Add(__ak); __thisOrderTemp.Add(null); }");
        code.CloseBlock();
        code.AppendLine(
            "foreach (var __tt in thisTempAddedOrder"
                + id
                + ") if (!__slotted.Contains(__tt)) { __thisOrder.Add("
                + sentinel
                + "); __thisOrderTemp.Add(__tt); __slotted.Add(__tt); }"
        );
        code.AppendLine(
            "if (__thisOrder.Count > 0) { result.__order = __thisOrder; result.__orderTemp = __thisOrderTemp; }"
        );
        code.CloseBlock();
    }
}
