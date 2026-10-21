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
        code.AppendLine("if (");
        code.AppendLine("(__removed is null || __removed.Count == 0)");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + ">? __simpleAdded = null;");
        code.AppendLine("if (__added is not null && __added.Count > 0)");
        code.OpenBlock();
        code.AppendLine(
            "__simpleAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__added.Count, "
                + comparer
                + ");"
        );
        code.AppendLine("foreach (var item in __added) __simpleAdded[" + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)] = item;");
        code.CloseBlock();
        code.AppendLine("var __simpleAddedCount = (__simpleAdded?.Count ?? 0) + (next.__added?.Count ?? 0);");
        code.AppendLine(
            "if (__simpleAddedCount > 0) result.__added = new global::System.Collections.Generic.List<" + elementType + ">(__simpleAddedCount);"
        );
        code.AppendLine("if (__simpleAdded is not null) foreach (var item in __simpleAdded.Values) result.__added!.Add(item);");
        code.AppendLine("if (next.__added is not null) result.__added!.AddRange(next.__added);");
        code.AppendLine("return result;");
        code.CloseBlock();
    }

    internal static void AppendAssignedAddThenRemoveCancellationFastPath(SharedIndentedBuilder code, string comparer, SparseMemberModel member)
    {
        code.AppendLine("if (");
        code.AppendLine("__added is not null && __added.Count == 1");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__removed is null || __removed.Count == 0)");
        code.AppendLine("&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine("&& " + comparer + ".Equals(" + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__added[0]), next.__removed[0])");
        code.AppendLine(") return result;");
    }

    internal static void AppendAssignedRemoveThenAddFastPath(SharedIndentedBuilder code, string keyType, string elementType)
    {
        code.AppendLine("if (");
        code.AppendLine("(__added is null || __added.Count == 0)");
        code.AppendLine("&& next.__added is not null && next.__added.Count > 0");
        code.AppendLine("&& __removed is not null && __removed.Count > 0");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("result.__removed = new global::System.Collections.Generic.List<" + keyType + ">(__removed);");
        code.AppendLine("result.__added = new global::System.Collections.Generic.List<" + elementType + ">(next.__added);");
        code.AppendLine("return result;");
        code.CloseBlock();
    }

    internal static void AppendAssignedOrderOnlyComposeFastPath(SharedIndentedBuilder code, string keyType)
    {
        code.AppendLine("if (");
        code.AppendLine("(__added is null || __added.Count == 0)");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__removed is null || __removed.Count == 0)");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("if (next.__order is not null && next.__order.Count > 0)");
        code.AppendLine("result.__order = new global::System.Collections.Generic.List<" + keyType + ">(next.__order);");
        code.AppendLine("else if (__order is not null && __order.Count > 0)");
        code.AppendLine("result.__order = new global::System.Collections.Generic.List<" + keyType + ">(__order);");
        code.AppendLine("return result;");
        code.CloseBlock();
    }

    internal static void AppendAssignedEditThenRemoveFastPath(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLine("if (");
        code.AppendLine("(__added is null || __added.Count == 0)");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__removed is null || __removed.Count == 0)");
        code.AppendLine("&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLine("&& __edited is not null && __edited.Count == 1");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("foreach (var __simpleEditKey in __edited.Keys)");
        code.OpenBlock();
        code.AppendLine("if (!" + comparer + ".Equals(__simpleEditKey, next.__removed[0])) continue;");
        code.AppendLine("result.__removed = new global::System.Collections.Generic.List<" + keyType + ">(next.__removed);");
        code.AppendLine("return result;");
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendAssignedRemoveOnlyComposeFastPath(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLine("if (");
        code.AppendLine("(__added is null || __added.Count == 0)");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("if (__removed is null || __removed.Count == 0)");
        code.OpenBlock();
        code.AppendLine("if (next.__removed is not null && next.__removed.Count > 0)");
        code.AppendLine("result.__removed = new global::System.Collections.Generic.List<" + keyType + ">(next.__removed);");
        code.CloseBlock();
        code.AppendLine("else if (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("result.__removed = new global::System.Collections.Generic.List<" + keyType + ">(__removed);");
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var __simpleRemovedSeen = new global::System.Collections.Generic.HashSet<" + keyType + ">(" + comparer + ");");
        code.AppendLine(
            "var __simpleRemoved = new global::System.Collections.Generic.List<" + keyType + ">(__removed.Count + next.__removed.Count);"
        );
        code.AppendLine("foreach (var key in __removed) if (__simpleRemovedSeen.Add(key)) __simpleRemoved.Add(key);");
        code.AppendLine("foreach (var key in next.__removed) if (__simpleRemovedSeen.Add(key)) __simpleRemoved.Add(key);");
        code.AppendLine("result.__removed = __simpleRemoved;");
        code.CloseBlock();
        code.AppendLine("return result;");
        code.CloseBlock();
    }

    internal static void AppendAssignedEditOnlyComposeFastPath(SharedIndentedBuilder code, string keyType, string comparer, SparseMemberModel member)
    {
        var elementPatchType = SparseKeyedCollectionEmitter.ElementPatchType(member);
        code.AppendLine("if (");
        code.AppendLine("(__added is null || __added.Count == 0)");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__removed is null || __removed.Count == 0)");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementPatchType + ">? __simpleEdited = null;");
        code.AppendLine("if (__edited is not null) foreach (var kv in __edited)");
        code.OpenBlock();
        code.AppendLine("if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var __nextEdit) && !__nextEdit.__SparseIsEmpty())");
        code.OpenBlock();
        code.AppendLine("var __composedEdit = kv.Value.Compose(__nextEdit);");
        code.AppendLine("if (__composedEdit.__SparseIsEmpty()) __simpleEdited?.Remove(kv.Key);");
        code.AppendLine(
            "else (__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = __composedEdit;"
        );
        code.CloseBlock();
        code.AppendLine("else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty())");
        code.AppendLine(
            "(__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = kv.Value;"
        );
        code.CloseBlock();
        code.AppendLine("if (next.__edited is not null) foreach (var kv in next.__edited)");
        code.OpenBlock();
        code.AppendLine("if (kv.Value.__SparseIsEmpty()) continue;");
        code.AppendLine("if (__simpleEdited is not null && __simpleEdited.ContainsKey(kv.Key)) continue;");
        code.AppendLine(
            "(__simpleEdited ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementPatchType
                + ">("
                + comparer
                + "))[kv.Key] = kv.Value;"
        );
        code.CloseBlock();
        code.AppendLine("if (__simpleEdited is not null && __simpleEdited.Count > 0) result.__edited = __simpleEdited;");
        code.AppendLine("return result;");
        code.CloseBlock();
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
        code.AppendLine("if (");
        code.AppendLine("(__removed is null || __removed.Count == 0)");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__order is null || __order.Count == 0)");
        code.AppendLine("&& (next.__order is null || next.__order.Count == 0)");
        code.AppendLine("&& __added is not null && __added.Count > 0");
        code.AppendLine("&& (next.__added is null || next.__added.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& next.__edited is not null && next.__edited.Count > 0");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine(
            "var __simpleAdded = new global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + ">(" + comparer + ");"
        );
        code.AppendLine("foreach (var item in __added) __simpleAdded[" + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)] = item;");
        code.AppendLine("var __simpleMatchesAdded = true;");
        code.AppendLine("foreach (var kv in next.__edited)");
        code.OpenBlock();
        code.AppendLine("if (!__simpleAdded.TryGetValue(kv.Key, out var __addedItem)) { __simpleMatchesAdded = false; break; }");
        code.AppendLine(
            "var __appliedEdit = kv.Value.Apply("
                + runtime
                + "Optional<"
                + elementFragment
                + "?>.Present("
                + elementFragment
                + ".From(__addedItem)));"
        );
        code.AppendLine(
            "if (!__appliedEdit.IsPresent || __appliedEdit.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
        );
        code.AppendLine(
            "if (!"
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__appliedEdit.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
        );
        code.AppendLine("__simpleAdded[kv.Key] = __appliedEdit.Value!.ToModel();");
        code.CloseBlock();
        code.AppendLine("if (__simpleMatchesAdded)");
        code.OpenBlock();
        code.AppendLine("result.__added = new global::System.Collections.Generic.List<" + elementType + ">(__simpleAdded.Values);");
        code.AppendLine("return result;");
        code.CloseBlock();
        code.CloseBlock();
    }
}
