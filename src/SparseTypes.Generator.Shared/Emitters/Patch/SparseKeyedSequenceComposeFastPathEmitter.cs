namespace SparseTypes.Generator.Shared;

internal static class SparseKeyedSequenceComposeFastPathEmitter
{
    internal static void AppendAssignedAddOnlyComposeFastPath(
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

    internal static void AppendAssignedAddThenRemoveCancellationFastPath(
        SharedIndentedBuilder code,
        string comparer,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "__added is not null && __added.Count == 1");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(
            5,
            "&& "
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__added[0]), next.__removed[0])"
        );
        code.AppendLineAt(4, ") return result;");
    }

    internal static void AppendAssignedRemoveThenAddFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string elementType,
        string comparer,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__added is null || __added.Count == 0)");
        code.AppendLineAt(5, "&& next.__added is not null && next.__added.Count == 1");
        code.AppendLineAt(5, "&& __removed is not null && __removed.Count == 1");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ("
                + comparer
                + ".Equals(__removed[0], "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(next.__added[0])))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__removed);"
        );
        code.AppendLineAt(
            6,
            "result.__added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(next.__added);"
        );
        code.AppendLineAt(6, "return result;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    internal static void AppendAssignedOrderOnlyComposeFastPath(
        SharedIndentedBuilder code,
        string keyType
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__added is null || __added.Count == 0)");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (next.__order is not null && next.__order.Count > 0)");
        code.AppendLineAt(
            6,
            "result.__order = new global::System.Collections.Generic.List<"
                + keyType
                + ">(next.__order);"
        );
        code.AppendLineAt(5, "else if (__order is not null && __order.Count > 0)");
        code.AppendLineAt(
            6,
            "result.__order = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__order);"
        );
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
    }

    internal static void AppendAssignedEditThenRemoveFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string comparer
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__added is null || __added.Count == 0)");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLineAt(5, "&& __edited is not null && __edited.Count == 1");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "foreach (var __simpleEditKey in __edited.Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!" + comparer + ".Equals(__simpleEditKey, next.__removed[0])) continue;"
        );
        code.AppendLineAt(
            6,
            "result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(next.__removed);"
        );
        code.AppendLineAt(6, "return result;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    internal static void AppendAssignedRemoveOnlyComposeFastPath(
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

    internal static void AppendAssignedEditOnlyComposeFastPath(
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

    internal static void AppendAssignedAddedThenEditedComposeFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string elementType,
        string comparer,
        SparseMemberModel member,
        string runtime
    )
    {
        var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__order is null || __order.Count == 0)");
        code.AppendLineAt(5, "&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLineAt(5, "&& __added is not null && __added.Count > 0");
        code.AppendLineAt(5, "&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& next.__edited is not null && next.__edited.Count > 0");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __simpleAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "foreach (var item in __added) __simpleAdded["
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(5, "var __simpleMatchesAdded = true;");
        code.AppendLineAt(5, "foreach (var kv in next.__edited)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__simpleAdded.TryGetValue(kv.Key, out var __addedItem)) { __simpleMatchesAdded = false; break; }"
        );
        code.AppendLineAt(
            6,
            "var __appliedEdit = kv.Value.Apply("
                + runtime
                + "Optional<"
                + elementFragment
                + "?>.Present("
                + elementFragment
                + ".From(__addedItem)));"
        );
        code.AppendLineAt(
            6,
            "if (!__appliedEdit.IsPresent || __appliedEdit.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
        );
        code.AppendLineAt(
            6,
            "if (!"
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__appliedEdit.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
        );
        code.AppendLineAt(6, "__simpleAdded[kv.Key] = __appliedEdit.Value!.ToModel();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__simpleMatchesAdded)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "result.__added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(__simpleAdded.Values);"
        );
        code.AppendLineAt(6, "return result;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }
}
