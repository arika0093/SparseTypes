namespace SparseTypes.Generator.Shared;

internal static class SparseDictionaryComposeFastPathEmitter
{
    internal static void AppendSetThenRemoveFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string comparer
    )
    {
        code.AppendLine("if (");
        code.AppendLine("__set is not null && __set.Count == 1");
        code.AppendLine("&& (next.__set is null || next.__set.Count == 0)");
        code.AppendLine("&& (__removed is null || __removed.Count == 0)");
        code.AppendLine("&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("foreach (var __setKey in __set.Keys)");
        code.OpenBlock();
        code.AppendLine("if (!" + comparer + ".Equals(__setKey, next.__removed[0])) continue;");
        code.AppendLine(
            "result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(next.__removed);"
        );
        code.AppendLine("return result;");
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendRemoveThenSetFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string valueType,
        string comparer
    )
    {
        code.AppendLine("if (");
        code.AppendLine("(__set is null || __set.Count == 0)");
        code.AppendLine("&& next.__set is not null && next.__set.Count == 1");
        code.AppendLine("&& __removed is not null && __removed.Count == 1");
        code.AppendLine("&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLine("&& (__edited is null || __edited.Count == 0)");
        code.AppendLine("&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLine(")");
        code.OpenBlock();
        code.AppendLine("foreach (var __setKey in next.__set.Keys)");
        code.OpenBlock();
        code.AppendLine("if (!" + comparer + ".Equals(__setKey, __removed[0])) continue;");
        code.AppendLine(
            "result.__set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(next.__set, "
                + comparer
                + ");"
        );
        code.AppendLine("return result;");
        code.CloseBlock();
        code.CloseBlock();
    }
}
