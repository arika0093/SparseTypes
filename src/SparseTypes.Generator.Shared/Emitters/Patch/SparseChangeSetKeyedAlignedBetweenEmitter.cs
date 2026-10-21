using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetKeyedAlignedBetweenEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string transition,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var key = KeyTypeOf(member);
        var element = ElementTypeOf(member);
        var items = "global::System.Collections.Generic.List<" + transition + ".Item>";
        var order = "global::System.Collections.Generic.List<" + key + ">";
        var sequence = "global::System.Collections.Generic.IEnumerable<" + element + ">";
        var indexed = "global::System.Collections.Generic.IReadOnlyList<" + element + ">";
        var keyOf = "__SparseKeyOf_ChangeSet_" + member.Id;
        var name = "__tryAligned" + member.Id;
        code.AppendLine(
            "static bool "
                + name
                + "("
                + sequence
                + " sourceBefore, "
                + sequence
                + " sourceAfter, out "
                + items
                + "? items, out "
                + order
                + "? order)"
        );
        code.OpenBlock();
        code.AppendLine("items = null; order = null;");
        code.AppendLine(
            "if (sourceBefore is not "
                + indexed
                + " b || sourceAfter is not "
                + indexed
                + " a) return false;"
        );
        code.AppendLine("var count = b.Count;");
        code.AppendLine("if (count != a.Count) return false;");
        code.AppendLine("if (count == 0) return true;");
        code.AppendLine(
            "var comparer = global::System.Collections.Generic.EqualityComparer<"
                + key
                + ">.Default;"
        );
        // Reject nonaligned inputs before allocating or comparing element
        // values.
        code.AppendLine("for (var i = 0; i < count; i++)");
        code.OpenBlock();
        code.AppendLine("if ((object?)b[i] is null || (object?)a[i] is null) return false;");
        code.AppendLine("var k = " + keyOf + "(b[i]);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") return false;"
            );
        }

        code.AppendLine("if (!comparer.Equals(k, " + keyOf + "(a[i]))) return false;");
        code.CloseBlock();
        if (dialect.KeyValidationScopeFactory is not null)
        {
            code.AppendLine("static void ValidateKeys(" + indexed + " b, int count)");
            code.OpenBlock();
            code.AppendLine(
                "using (var keyScope = "
                    + dialect.KeyValidationScopeFactory
                    + "<"
                    + key
                    + ">(count, static capacity => new global::System.Collections.Generic.HashSet<"
                    + key
                    + ">("
                    + (dialect.HashSetSupportsCapacity ? "capacity, " : "")
                    + "global::System.Collections.Generic.EqualityComparer<"
                    + key
                    + ">.Default)))"
            );
            code.OpenBlock();
            code.AppendLine("var seen = keyScope.Keys;");
            AppendValidation(code, keyOf, true);
            code.CloseBlock();
            code.CloseBlock();
            code.AppendLine("ValidateKeys(b, count);");
        }
        else
        {
            var seenType = dialect.HashSetSupportsCapacity
                ? "global::System.Collections.Generic.HashSet<" + key + ">"
                : "global::System.Collections.Generic.Dictionary<" + key + ", byte>";
            code.AppendLine("var seen = new " + seenType + "(count, comparer);");
            AppendValidation(code, keyOf, dialect.HashSetSupportsCapacity);
        }
        code.AppendLine("for (var i = 0; i < count; i++)");
        code.OpenBlock();
        code.AppendLine("var edit = " + ElementChangeSetOf(member) + ".Between(b[i], a[i]);");
        code.AppendLine("if (edit.IsEmpty) continue;");
        code.AppendLine("items ??= new " + items + "();");
        code.AppendLine(
            "items.Add(new "
                + transition
                + ".Item("
                + keyOf
                + "(b[i]), null, default, default, i, i, false, false, true, false, edit, false));"
        );
        code.CloseBlock();
        // Unchanged collections need validation, but no retained order storage.
        code.AppendLine("if (items is not null)");
        code.OpenBlock();
        code.AppendLine("order = new " + order + "(count);");
        code.AppendLine("for (var i = 0; i < count; i++) order.Add(" + keyOf + "(b[i]));");
        code.CloseBlock();
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine(
            "if ("
                + name
                + "(__before"
                + member.Id
                + ".Value!, __after"
                + member.Id
                + ".Value!, out __items"
                + member.Id
                + ", out var __alignedOrder"
                + member.Id
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("__h" + member.Id + " = __items" + member.Id + " is not null;");
        code.AppendLine(
            "__bO"
                + member.Id
                + " = __alignedOrder"
                + member.Id
                + "; __aO"
                + member.Id
                + " = __alignedOrder"
                + member.Id
                + ";"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
    }

    private static void AppendValidation(SharedIndentedBuilder code, string keyOf, bool useSet)
    {
        code.AppendLine("for (var i = 0; i < count; i++)");
        code.OpenBlock();
        code.AppendLine("var k = " + keyOf + "(b[i]);");
        if (useSet)
        {
            code.AppendLine(
                "if ((object?)k is null) throw new global::System.ArgumentNullException(\"key\");"
            );
        }

        code.AppendLine(
            useSet
                ? "if (!seen.Add(k)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
                : SparseKeyedCollectionEmitter.AddUniqueEntry("seen", "k", "0")
        );
        code.CloseBlock();
    }

    internal static void AppendGeneralStart(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string transition,
        string runtime
    )
    {
        var id = member.Id;
        var items = "global::System.Collections.Generic.List<" + transition + ".Item>";
        var order = "global::System.Collections.Generic.List<" + KeyTypeOf(member) + ">";
        var optional = runtime + "Optional<" + FragmentValueType(member) + ">";
        var parameters = optional + " __before" + id + ", " + optional + " __after" + id;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            var map =
                "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + ElementTypeOf(member)
                + ">";
            var temporaryOrder = "global::System.Collections.Generic.List<global::System.Guid>";
            parameters +=
                ", "
                + map
                + " __bT"
                + id
                + ", "
                + temporaryOrder
                + " __bTO"
                + id
                + ", "
                + map
                + " __aT"
                + id
                + ", "
                + temporaryOrder
                + " __aTO"
                + id;
        }
        // Give the general path its own JIT profile instead of leaving it cold
        // behind alignment.
        code.AppendLine(
            "static (bool Has, "
                + items
                + "? Items, "
                + order
                + "? BeforeOrder, "
                + order
                + "? AfterOrder) __general"
                + id
                + "("
                + parameters
                + ")"
        );
        code.OpenBlock();
        code.AppendLine("bool __h" + id + " = false;");
        code.AppendLine(items + "? __items" + id + " = null;");
        code.AppendLine(order + "? __bO" + id + " = null;");
        code.AppendLine(order + "? __aO" + id + " = null;");
    }

    internal static void AppendGeneralEnd(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var id = member.Id;
        code.AppendLine(
            "return (__h" + id + ", __items" + id + ", __bO" + id + ", __aO" + id + ");"
        );
        code.CloseBlock();
        var arguments = "__before" + id + ", __after" + id;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            arguments += ", __bT" + id + ", __bTO" + id + ", __aT" + id + ", __aTO" + id;
        }

        code.AppendLine(
            "(__h"
                + id
                + ", __items"
                + id
                + ", __bO"
                + id
                + ", __aO"
                + id
                + ") = __general"
                + id
                + "("
                + arguments
                + ");"
        );
    }
}
