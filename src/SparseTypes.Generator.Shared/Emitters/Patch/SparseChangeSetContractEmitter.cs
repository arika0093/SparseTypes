using System.Collections.Generic;
using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Read-only transition-contract surface for generated ChangeSets.</summary>
/// <remarks>Emits the product-configured change-set contract.</remarks>
internal static class SparseChangeSetContractEmitter
{
    /// <summary>Nullability contract for the <c>TryGet*</c> out parameters.</summary>
    private const string NotNullWhenTrue = "[global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ";

    internal static bool HasContracts(SparseFragmentPatchEmitter.SparsePatchDialect dialect) => dialect.TransitionContracts is not null;

    internal static void AppendInterfaceMemberDocumentation(SharedIndentedBuilder code, bool hasContracts, string summary)
    {
        code.AppendLine(hasContracts ? "/// <inheritdoc />" : "/// <summary>" + summary + "</summary>");
    }

    internal static string ChangeSetBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string? modelType)
    {
        if (!HasContracts(dialect))
        {
            return string.Empty;
        }

        var contracts = dialect.TransitionContracts!;
        if (modelType is null)
        {
            return " : " + contracts.ChangeSetInterfaceType;
        }

        var interfaces = new List<string>();
        if (contracts.TypedChangeSetInterfaceType is string typedInterfaceType)
        {
            interfaces.Add(typedInterfaceType + "<" + modelType + ">");
        }

        if (contracts.PipelineChangeSetInterfaceType is string pipelineInterfaceType)
        {
            interfaces.Add(pipelineInterfaceType + "<" + modelType + ", ChangeSet>");
        }

        return interfaces.Count == 0 ? " : " + contracts.ChangeSetInterfaceType : " : " + string.Join(", ", interfaces);
    }

    internal static string ScalarBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string valueType) =>
        HasContracts(dialect) ? " : " + dialect.TransitionContracts!.ValueChangeInterfaceType + "<" + valueType + ">" : string.Empty;

    internal static string KeyedBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, SparseMemberModel member)
    {
        if (!HasContracts(dialect))
        {
            return string.Empty;
        }

        var contracts = dialect.TransitionContracts!;
        var open = SparseKeyedCollectionEmitter.HasTemporaryKey(member)
            ? contracts.TemporaryKeyedChangeInterfaceType
            : contracts.KeyedChangeInterfaceType;
        return ", " + open + "<" + KeyTypeOf(member) + ", " + ElementTypeOf(member) + ", " + ElementChangeSetOf(member) + ">";
    }

    internal static string KeyedItemBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, SparseMemberModel member) =>
        HasContracts(dialect)
            ? " : "
                + dialect.TransitionContracts!.KeyedItemChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ElementTypeOf(member)
                + ", "
                + ElementChangeSetOf(member)
                + ">"
            : string.Empty;

    internal static string DictionaryBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, SparseMemberModel member)
    {
        if (!HasContracts(dialect))
        {
            return string.Empty;
        }

        var contracts = dialect.TransitionContracts!;
        return ", "
            + contracts.DictionaryChangeInterfaceType
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ValueTypeOf(member)
            + ", "
            + DictEditedType(member)
            + ">";
    }

    internal static string DictionaryItemBases(SparseFragmentPatchEmitter.SparsePatchDialect dialect, SparseMemberModel member)
    {
        if (!HasContracts(dialect))
        {
            return string.Empty;
        }

        var contracts = dialect.TransitionContracts!;
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            return " : "
                + contracts.StructuralDictionaryItemChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ", "
                + ValueChangeSetOf(member)
                + ">";
        }

        return " : " + contracts.DictionaryItemChangeInterfaceType + "<" + KeyTypeOf(member) + ", " + ValueTypeOf(member) + ">";
    }

    internal static bool IsStructuralDict(SparseMemberModel member) => member.Collection.ValueType?.IsFragmentModel == true;

    internal static string DictEditedType(SparseMemberModel member) => IsStructuralDict(member) ? ValueChangeSetOf(member) : ValueTypeOf(member);

    /// <summary>Appends explicit keyed lookup/enumeration contract members.</summary>
    /// <remarks>The public typed <c>GetChange</c> and <c>IEnumerable&lt.</remarks>
    internal static void AppendKeyedContractMembers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
        {
            return;
        }

        var contracts = dialect.TransitionContracts!;
        // GetChange lives on the base keyed contract even for temporary
        // members, while GetTemporaryChange lives on the derived temporary
        var keyed =
            contracts.KeyedChangeInterfaceType + "<" + KeyTypeOf(member) + ", " + ElementTypeOf(member) + ", " + ElementChangeSetOf(member) + ">";
        var item =
            contracts.KeyedItemChangeInterfaceType + "<" + KeyTypeOf(member) + ", " + ElementTypeOf(member) + ", " + ElementChangeSetOf(member) + ">";
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(item + " " + keyed + ".GetChange(" + KeyTypeOf(member) + " key) => GetChange(key);");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            var temporary =
                contracts.TemporaryKeyedChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ElementTypeOf(member)
                + ", "
                + ElementChangeSetOf(member)
                + ">";
            code.AppendLine("/// <inheritdoc />");
            code.AppendLine(item + " " + temporary + ".GetTemporaryChange(global::System.Guid temporaryKey) => GetTemporaryChange(temporaryKey);");
        }
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "global::System.Collections.Generic.IEnumerable<"
                + item
                + "> "
                + keyed
                + ".EnumerateItems() => global::System.Linq.Enumerable.Select(_items, static item => ("
                + item
                + ")item);"
        );
    }

    /// <summary>Appends explicit dictionary lookup/enumeration contract members.</summary>
    internal static void AppendDictionaryContractMembers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
        {
            return;
        }

        var contracts = dialect.TransitionContracts!;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dict = contracts.DictionaryChangeInterfaceType + "<" + keyType + ", " + valueType + ", " + DictEditedType(member) + ">";
        var item = contracts.DictionaryItemChangeInterfaceType + "<" + keyType + ", " + valueType + ">";
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(item + " " + dict + ".GetChange(" + keyType + " key) => GetChange(key);");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "global::System.Collections.Generic.IEnumerable<"
                + item
                + "> "
                + dict
                + ".EnumerateEntries() => global::System.Linq.Enumerable.Select(_items, static item => ("
                + item
                + ")item);"
        );
    }

    /// <summary>Appends the ChangeSet-level contract: emptiness plus traversal entry points.</summary>
    /// <remarks>Everything projects the existing typed surface (properties.</remarks>
    internal static void AppendChangeSetContract(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
        {
            return;
        }

        var contracts = dialect.TransitionContracts!;
        var iface = contracts.ChangeSetInterfaceType;
        ComputePublicNames(members, out var propNames, out _);
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "global::System.Collections.Generic.IReadOnlyList<" + contracts.ChangeRecordType + "> " + iface + ".EnumerateChangeRecords()"
        );
        code.OpenBlock();
        code.AppendLine("var records = new global::System.Collections.Generic.List<" + contracts.ChangeRecordType + ">();");
        code.AppendLine("foreach (var change in EnumerateChanges())");
        code.AppendLine("records.Add(change);");
        code.AppendLine("return records.AsReadOnly();");
        code.CloseBlock();
        AppendTryGetNested(code, members, propNames, iface);
        AppendTryGetValue(code, members, propNames, dialect, iface);
        AppendTryGetKeyed(code, members, propNames, dialect, iface);
        AppendTryGetDictionary(code, members, propNames, dialect, iface);
    }

    private static void AppendTryGetNested(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        string iface
    )
    {
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("bool " + iface + ".TryGetNestedChange(string memberName, " + NotNullWhenTrue + "out " + iface + "? child)");
        code.OpenBlock();
        foreach (var member in members)
        {
            if (!IsNested(member))
            {
                continue;
            }

            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLine(
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal)) { child = "
                    + prop
                    + "; return true; }"
            );
        }
        code.AppendLine("child = null;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendTryGetValue(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "bool "
                + iface
                + ".TryGetValueChange<T>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.ValueChangeInterfaceType
                + "<T>? change)"
        );
        code.OpenBlock();
        code.AppendLine("if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (IsNested(member) || IsKeyed(member) || IsDict(member))
            {
                continue;
            }

            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            // Exact-match dispatch through interface pattern matching.
            code.AppendLine(
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.ValueChangeInterfaceType
                    + "<T> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLine("change = null;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendTryGetKeyed(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "bool "
                + iface
                + ".TryGetKeyedChange<TKey, TElement, TElementChange>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.KeyedChangeInterfaceType
                + "<TKey, TElement, TElementChange>? change)"
        );
        code.OpenBlock();
        code.AppendLine("if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (!IsKeyed(member))
            {
                continue;
            }

            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLine(
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.KeyedChangeInterfaceType
                    + "<TKey, TElement, TElementChange> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLine("change = null;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendTryGetDictionary(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "bool "
                + iface
                + ".TryGetDictionaryChange<TKey, TValue, TEdited>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.DictionaryChangeInterfaceType
                + "<TKey, TValue, TEdited>? change)"
        );
        code.OpenBlock();
        code.AppendLine("if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (!IsDict(member))
            {
                continue;
            }

            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLine(
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.DictionaryChangeInterfaceType
                    + "<TKey, TValue, TEdited> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLine("change = null;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }
}
