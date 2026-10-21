namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity fragments for keyed-sequence transitions.</summary>
/// <remarks>Hosts the Guid identity surface of keyed transition items (<c>TemporaryKey</c>.</remarks>
internal static class SparseChangeSetKeyedTempTransitionEmitter
{
    /// <summary>Emits the TemporaryKey property of a keyed transition item.</summary>
    internal static void AppendItemTemporaryKeyProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLine(
            "/// <summary>Gets the temporary identity for unassigned entries.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Non-null only when the entry key is unassigned and the element type opts into temporary identity; assigned keys always take precedence.</remarks>"
        );
        code.AppendLine(
            SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                ? "public global::System.Guid? TemporaryKey { get; }"
                : "public global::System.Guid? TemporaryKey => null;"
        );
    }

    /// <summary>Emits the temporary-identity lookup of a keyed transition.</summary>
    internal static void AppendGetTemporaryChange(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLine(
            "/// <summary>Looks up the typed change for a temporary identity; never returns null.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Only entries with an unassigned permanent key participate: a valid assigned key always wins, so assigned entries are never returned here even when they retain a temporary value. Unknown, empty, or temporary-less entries return <see cref=\"Item.Empty\"/>. Non-empty results are the same instances produced by enumeration.</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"temporaryKey\">The temporary identity to look up.</param>"
        );
        code.AppendLine("/// <returns>The typed change for the identity.</returns>");
        code.AppendLine("public Item GetTemporaryChange(global::System.Guid temporaryKey)");
        code.OpenBlock();
        code.AppendLine("if (temporaryKey == global::System.Guid.Empty) return Item.Empty;");
        code.AppendLine("var __lookup = _temporaryLookup;");
        code.AppendLine("if (__lookup is null)");
        code.OpenBlock();
        code.AppendLine("int __lookupCount = 0;");
        code.AppendLine(
            "for (int __index = 0; __index < _items.Count; __index++) if (_items[__index].TemporaryKey.HasValue && "
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "_items[__index].Key")
                + ") __lookupCount++;"
        );
        code.AppendLine(
            "__lookup = new global::System.Collections.Generic.Dictionary<global::System.Guid, Item>(__lookupCount);"
        );
        code.AppendLine("foreach (var __item in _items)");
        code.AppendLine(
            "if (__item.TemporaryKey.HasValue && "
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__item.Key")
                + ") __lookup[__item.TemporaryKey.Value] = __item;"
        );
        code.AppendLine("_temporaryLookup = __lookup;");
        code.CloseBlock();
        code.AppendLine(
            "return __lookup.TryGetValue(temporaryKey, out var __found) ? __found : Item.Empty;"
        );
        code.CloseBlock();
    }

    /// <summary>Emits the temporary-lookup backing field of a keyed transition.</summary>
    internal static void AppendTemporaryLookupField(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "private global::System.Collections.Generic.Dictionary<global::System.Guid, Item>? _temporaryLookup;"
        );
    }

    /// <summary>Emits the ChangeSet-side temporary-identity extractor.</summary>
    internal static void AppendTemporaryKeyExtractor(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool documented
    )
    {
        if (documented)
        {
            code.AppendLine(
                "/// <summary>Extracts the temporary identity for a keyed transition element.</summary>"
            );
        }
        code.AppendLine(
            "private static global::System.Guid? __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "("
                + elementType
                + " element)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no temporary identity.\");"
        );
        code.AppendLine(
            "return element." + SparseKeyedCollectionEmitter.TemporaryKeyProperty(member) + ";"
        );
        code.CloseBlock();
    }

    /// <summary>Emits whole-path temporary capture declarations.</summary>
    internal static void AppendBuildTempDeclarations(SharedIndentedBuilder code, string elementType)
    {
        code.AppendLine(
            "var __seenBuildTemp = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLine(
            "var __unassignedBefore = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        code.AppendLine(
            "var __unassignedTempBefore = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLine(
            "var __unassignedTempBeforeIndex = new global::System.Collections.Generic.List<int>();"
        );
        code.AppendLine(
            "var __unassignedTempAfter = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
    }

    /// <summary>Emits the whole-path before-walk temporary branch.</summary>
    internal static void AppendBuildTempBeforeBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        // Whole-transition snapshots may carry temporary identities as data.
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __bt = __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "(__item); if (!__bt.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__bt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); if (!__seenBuildTemp.Add(__bt.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __unassignedTempBeforeIndex.Add(__beforeOrder.Count); __beforeOrder.Add(__k); __unassignedBefore.Add(__item); __unassignedTempBefore.Add(__bt.Value); continue; }"
        );
    }

    /// <summary>Emits the whole-path after-walk temporary branch.</summary>
    internal static void AppendBuildTempAfterBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __at = __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "(__item); if (!__at.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__at.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); if (!__seenBuildTemp.Add(__at.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __afterOrder.Add(__k!); __unassignedAfter.Add(__item); __unassignedTempAfter.Add(__at.Value); continue; }"
        );
    }

    /// <summary>Emits whole-path temporary removals in before order.</summary>
    internal static void AppendBuildTempRemovedLoop(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType
    )
    {
        // Whole-path temporary removals in before order; at most one side
        // is populated, so positional temp capture above stays exact.
        code.AppendLine("for (var __tbi = 0; __tbi < __unassignedBefore.Count; __tbi++)");
        code.OpenBlock();
        code.AppendLine("if (__afterTempSet.Contains(__unassignedTempBefore[__tbi])) continue;");
        code.AppendLine("var __trb = __unassignedBefore[__tbi];");
        code.AppendLine("var __trt = __unassignedTempBefore[__tbi];");
        code.AppendLine(
            runtime
                + "Optional<"
                + elementFrag
                + "?> __treb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__trb));"
        );
        code.AppendLine("var __trfullEdit = " + elementCs + ".Between(__treb, default);");
        code.AppendLine(
            "__items.Add(new "
                + trans
                + ".Item(__SparseKeyOf_ChangeSet_"
                + member.Id
                + "(__trb), __trt, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__trb), default, __unassignedTempBeforeIndex[__tbi], -1, false, true, false, false, __trfullEdit, false));"
        );
        code.CloseBlock();
    }

    /// <summary>Emits the whole-path unassigned-add branch with temp stamps.</summary>
    internal static void AppendBuildTempAddBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType
    )
    {
        code.AppendLine(
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __upos = __unassignedPosition++; var __ua = __unassignedAfter[__upos]; var __ut = __unassignedTempAfter[__upos]; var __uaEdit = "
                + elementCs
                + ".Between(default, "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ua))); __items.Add(new "
                + trans
                + ".Item(__k!, __ut, default, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__ua), -1, __afterPosition++, true, false, false, false, __uaEdit, false)); continue; }"
        );
    }
}
