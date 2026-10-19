namespace SparseTypes.Generator.Shared;

internal static class SparseDictionaryComposeFastPathEmitter
{
    internal static void AppendSetThenRemoveFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string comparer
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "__set is not null && __set.Count == 1");
        code.AppendLineAt(5, "&& (next.__set is null || next.__set.Count == 0)");
        code.AppendLineAt(5, "&& (__removed is null || __removed.Count == 0)");
        code.AppendLineAt(5, "&& next.__removed is not null && next.__removed.Count == 1");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "foreach (var __setKey in __set.Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!" + comparer + ".Equals(__setKey, next.__removed[0])) continue;"
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

    internal static void AppendRemoveThenSetFastPath(
        SharedIndentedBuilder code,
        string keyType,
        string valueType,
        string comparer
    )
    {
        code.AppendLineAt(4, "if (");
        code.AppendLineAt(5, "(__set is null || __set.Count == 0)");
        code.AppendLineAt(5, "&& next.__set is not null && next.__set.Count == 1");
        code.AppendLineAt(5, "&& __removed is not null && __removed.Count == 1");
        code.AppendLineAt(5, "&& (next.__removed is null || next.__removed.Count == 0)");
        code.AppendLineAt(5, "&& (__edited is null || __edited.Count == 0)");
        code.AppendLineAt(5, "&& (next.__edited is null || next.__edited.Count == 0)");
        code.AppendLineAt(4, ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "foreach (var __setKey in next.__set.Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (!" + comparer + ".Equals(__setKey, __removed[0])) continue;");
        code.AppendLineAt(
            6,
            "result.__set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(next.__set, "
                + comparer
                + ");"
        );
        code.AppendLineAt(6, "return result;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }
}
