using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Collection branches of the resolution fragment applier.</summary>
/// <remarks>Handles keyed sequences, dictionaries, and whole-collection members.</remarks>
internal static class SparseResolutionCollectionEmitter
{
    internal static void AppendKeyHelpers(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members
    )
    {
        foreach (var member in members)
        {
            if (!IsKeyed(member))
            {
                continue;
            }

            var keyType = KeyTypeOf(member);
            var elementType = ElementTypeOf(member);
            var keys = member.Collection.KeyPropertyNames;
            // Single-property keys only: the key type is the declared property
            // type, which may itself be a tuple or value object.
            string extraction;
            if (!keys.IsDefault && keys.Length == 1)
            {
                extraction =
                    "element."
                    + SparseNaming.EscapeIdentifier(keys[0])
                    + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : string.Empty)
                    + ";";
            }
            else
            {
                extraction =
                    "throw new global::System.InvalidOperationException(\"Keyed collection has no usable key property.\");";
            }

            body.AppendLine(
                "/// <summary>Extracts the stable key of one element (resolution infrastructure).</summary>"
            );
            body.AppendLine(
                "private static "
                    + keyType
                    + " __SparseResolutionKeyOf_"
                    + member.Id
                    + "("
                    + elementType
                    + " element)"
            );
            body.OpenBlock();
            body.AppendLine(
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            body.AppendLine("return " + extraction);
            body.CloseBlock();
        }
    }

    internal static void AppendKeyedBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime,
        string? workspace
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementFragment = ElementFragmentOf(member);
        var elementChangeSet = ElementChangeSetOf(member);
        var listType = "global::System.Collections.Generic.List<" + elementType + ">";
        var readOnlyListType =
            "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
        var orderListType = "global::System.Collections.Generic.List<" + keyType + ">";
        var segmentKind = runtime + "SparsePathSegmentKind";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var id = member.Id;
        var slotIsArray = valueType.EndsWith("[]", System.StringComparison.Ordinal);
        var slotValue = slotIsArray ? "list" + id + ".ToArray()" : "list" + id;
        var keyOf = "__SparseResolutionKeyOf_" + id;
        var elementIsRef = member.Collection.ElementType.IsReferenceType;

        // Member-level paths address either the whole collection or, for keyed
        // sequences, the order transition.
        body.AppendLine("if (offset + 1 == path.Depth)");
        body.OpenBlock();
        body.AppendLine(
            "if (desired.IsPresent && desired.Value is "
                + orderListType
                + " orderDesired"
                + id
                + ")"
        );
        body.OpenBlock();
        AppendKeyedOrder(
            body,
            esc,
            name,
            runtime,
            optional,
            valueType,
            keyType,
            elementType,
            listType,
            readOnlyListType,
            slotValue,
            keyOf,
            elementIsRef,
            id,
            useDesiredVariable: true
        );
        body.CloseBlock();
        SparseResolutionApplyEmitter.AppendCheckedSlot(
            body,
            "builder." + esc,
            optional,
            valueType,
            name,
            id,
            rejectNull: false
        );
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (offset + 2 > path.Depth)");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution path is too short for member '" + name + "'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var keySegment" + id + " = path.Segments[offset + 1];");
        body.AppendLine("var rawKey" + id + " = keySegment" + id + ".Key;");
        body.AppendLine(
            "if (keySegment"
                + id
                + ".Kind != "
                + segmentKind
                + ".Key || rawKey"
                + id
                + " is null || !typeof("
                + keyType
                + ").IsInstanceOfType(rawKey"
                + id
                + "))"
        );
        body.OpenBlock();
        body.AppendLine(
            "error = \"The key segment for member '" + name + "' has an unexpected type.\";"
        );
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var key" + id + " = (" + keyType + ")rawKey" + id + ";");
        if (!member.Collection.ElementType.IsFragmentModel)
        {
            body.AppendLine("if (offset + 2 != path.Depth)");
            body.OpenBlock();
            body.AppendLine("error = \"Member '" + name + "' has no resolvable element leaves.\";");
            body.AppendLine("return false;");
            body.CloseBlock();
        }

        body.AppendLine("if (!builder." + esc + ".IsPresent || builder." + esc + ".Value is null)");
        body.OpenBlock();
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        AppendKeyedAbsentAdd(
            body,
            esc,
            optional,
            elementType,
            elementFragment,
            id,
            name,
            slotIsArray,
            member.Collection.ElementType.IsFragmentModel
        );
        body.CloseBlock();
        body.AppendLine(
            "if (builder." + esc + ".Value is not " + readOnlyListType + " source" + id + ")"
        );
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' has an unsupported collection shape.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        if (workspace is not null)
        {
            // Detached staging: copy once per build, then reuse while the slot
            // still holds the published working buffer.
            body.AppendLine(
                "global::System.Collections.Generic.List<" + elementType + "> list" + id + ";"
            );
            body.AppendLine(
                "var stageKey" + id + " = " + workspace + ".StagingKey(path, offset + 1);"
            );
            body.AppendLine(
                "if (workspace.TryGetStaged(stageKey"
                    + id
                    + ", out var staged"
                    + id
                    + ") && global::System.Object.ReferenceEquals(source"
                    + id
                    + ", staged"
                    + id
                    + "))"
            );
            body.OpenBlock();
            body.AppendLine("list" + id + " = (" + listType + ")staged" + id + "!;");
            body.CloseBlock();
            body.AppendLine("else");
            body.OpenBlock();
            body.AppendLine("list" + id + " = new " + listType + "(source" + id + ");");
            body.AppendLine("workspace.StoreStaged(stageKey" + id + ", list" + id + ");");
            body.CloseBlock();
        }
        else
        {
            body.AppendLine("var list" + id + " = new " + listType + "(source" + id + ");");
        }

        body.AppendLine("var index" + id + " = -1;");
        body.AppendLine(
            "for (var scan" + id + " = 0; scan" + id + " < list" + id + ".Count; scan" + id + "++)"
        );
        body.OpenBlock();
        if (elementIsRef)
        {
            body.AppendLine("var candidate" + id + " = list" + id + "[scan" + id + "];");
            body.AppendLine("if (candidate" + id + " is null) continue;");
        }
        else
        {
            body.AppendLine("var candidate" + id + " = list" + id + "[scan" + id + "];");
        }

        body.AppendLine(
            "if ("
                + comparer
                + ".Equals("
                + keyOf
                + "(candidate"
                + id
                + "), key"
                + id
                + ")) { index"
                + id
                + " = scan"
                + id
                + "; break; }"
        );
        body.CloseBlock();
        if (member.Collection.ElementType.IsFragmentModel)
        {
            body.AppendLine("if (offset + 2 == path.Depth)");
            body.OpenBlock();
            AppendKeyedElementWholesale(
                body,
                esc,
                optional,
                name,
                elementType,
                elementFragment,
                id,
                slotValue,
                elementIsModel: true
            );
            body.CloseBlock();
            body.AppendLine("if (index" + id + " < 0)");
            body.OpenBlock();
            body.AppendLine(
                "error = \"No element with the key was found in member '" + name + "'.\";"
            );
            body.AppendLine("return false;");
            body.CloseBlock();
            if (elementIsRef)
            {
                body.AppendLine("if (list" + id + "[index" + id + "] is null)");
                body.OpenBlock();
                body.AppendLine("error = \"The element for member '" + name + "' is missing.\";");
                body.AppendLine("return false;");
                body.CloseBlock();
            }

            body.AppendLine(
                "var elementFragment"
                    + id
                    + " = "
                    + elementFragment
                    + ".From(list"
                    + id
                    + "[index"
                    + id
                    + "]!);"
            );
            body.AppendLine(
                "if (!"
                    + elementChangeSet
                    + ".__SparseTryApplyResolution(elementFragment"
                    + id
                    + ", path, offset + 2, desired"
                    + SparseResolutionApplyEmitter.WorkspaceArgument(workspace)
                    + ", out var updatedElement"
                    + id
                    + ", out error)) return false;"
            );
            body.AppendLine("if (updatedElement" + id + " is null)");
            body.OpenBlock();
            body.AppendLine("error = \"The element resolution produced no fragment.\";");
            body.AppendLine("return false;");
            body.CloseBlock();
            body.AppendLine(
                "list" + id + "[index" + id + "] = updatedElement" + id + ".ToModel();"
            );
            body.AppendLine("builder." + esc + " = " + optional + ".Present(" + slotValue + ");");
            body.AppendLine("updated = builder.Build();");
            body.AppendLine("return true;");
        }
        else
        {
            body.OpenBlock();
            AppendKeyedElementWholesale(
                body,
                esc,
                optional,
                name,
                elementType,
                elementFragment,
                id,
                slotValue,
                elementIsModel: false
            );
            body.CloseBlock();
        }
    }

    private static void AppendKeyedAbsentAdd(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string elementType,
        string elementFragment,
        int id,
        string name,
        bool slotIsArray,
        bool elementIsModel
    )
    {
        var checkElement = SparseResolutionApplyEmitter.NonNullable(elementType);
        var seedSuffix = slotIsArray ? ".ToArray()" : string.Empty;
        body.AppendLine("if (desired.Value is " + checkElement + " fresh" + id + ")");
        body.OpenBlock();
        body.AppendLine(
            "builder."
                + esc
                + " = "
                + optional
                + ".Present(new global::System.Collections.Generic.List<"
                + elementType
                + "> { fresh"
                + id
                + " }"
                + seedSuffix
                + ");"
        );
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        if (elementIsModel)
        {
            body.AppendLine(
                "if (desired.Value is " + elementFragment + " freshFragment" + id + ")"
            );
            body.OpenBlock();
            body.AppendLine(
                "builder."
                    + esc
                    + " = "
                    + optional
                    + ".Present(new global::System.Collections.Generic.List<"
                    + elementType
                    + "> { freshFragment"
                    + id
                    + ".ToModel() }"
                    + seedSuffix
                    + ");"
            );
            body.AppendLine("updated = builder.Build();");
            body.AppendLine("return true;");
            body.CloseBlock();
        }
        body.AppendLine(
            "error = \"Member '" + name + "' is absent and the resolution value cannot seed it.\";"
        );
        body.AppendLine("return false;");
    }

    private static void AppendKeyedElementWholesale(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string elementType,
        string elementFragment,
        int id,
        string slotValue,
        bool elementIsModel
    )
    {
        var checkElement = SparseResolutionApplyEmitter.NonNullable(elementType);
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("if (index" + id + " >= 0) list" + id + ".RemoveAt(index" + id + ");");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is null)");
        body.OpenBlock();
        body.AppendLine("error = \"Null elements are not supported for member '" + name + "'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + checkElement + " element" + id + ")");
        body.OpenBlock();
        body.AppendLine(
            "if (index" + id + " >= 0) list" + id + "[index" + id + "] = element" + id + ";"
        );
        body.AppendLine("else list" + id + ".Add(element" + id + ");");
        body.CloseBlock();
        if (elementIsModel)
        {
            body.AppendLine(
                "else if (desired.Value is " + elementFragment + " wholeFragment" + id + ")"
            );
            body.OpenBlock();
            body.AppendLine(
                "var wholeModel"
                    + id
                    + " = wholeFragment"
                    + id
                    + ".ToModel(); if (index"
                    + id
                    + " >= 0) list"
                    + id
                    + "[index"
                    + id
                    + "] = wholeModel"
                    + id
                    + "; else list"
                    + id
                    + ".Add(wholeModel"
                    + id
                    + ");"
            );
            body.CloseBlock();
        }
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine(
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("builder." + esc + " = " + optional + ".Present(" + slotValue + ");");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }

    private static void AppendKeyedOrder(
        SharedIndentedBuilder body,
        string esc,
        string name,
        string runtime,
        string optional,
        string valueType,
        string keyType,
        string elementType,
        string listType,
        string readOnlyListType,
        string slotValue,
        string keyOf,
        bool elementIsRef,
        int id,
        bool useDesiredVariable = false
    )
    {
        _ = runtime;
        _ = valueType;

        // Order resolutions carry the desired key list.
        var orderValue = useDesiredVariable ? "orderDesired" + id : "order" + id;
        if (!useDesiredVariable)
        {
            body.AppendLine(
                "if (desired.Value is not global::System.Collections.Generic.List<"
                    + keyType
                    + "> order"
                    + id
                    + ")"
            );
            body.OpenBlock();
            body.AppendLine("error = \"The order resolution value must be a key list.\";");
            body.AppendLine("return false;");
            body.CloseBlock();
        }

        body.AppendLine("if (!builder." + esc + ".IsPresent || builder." + esc + ".Value is null)");
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' is absent and has no order.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine(
            "if (builder." + esc + ".Value is not " + readOnlyListType + " orderSource" + id + ")"
        );
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' has an unsupported collection shape.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        // One source-side key index replaces the quadratic per-key scans.
        // Duplicates and unknown keys stay lazily reported per requested key
        // so unmentioned duplicate rows keep their historical pass.
        body.AppendLine(
            "var orderComparer"
                + id
                + " = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default;"
        );
        body.AppendLine(
            "var reordered" + id + " = new " + listType + "(orderSource" + id + ".Count);"
        );
        body.AppendLine(
            "var orderIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", ("
                + elementType
                + " element, int matches)>(orderSource"
                + id
                + ".Count);"
        );
        body.AppendLine(
            "global::System.Collections.Generic.List<"
                + elementType
                + ">? orderNull"
                + id
                + " = null;"
        );
        body.AppendLine("foreach (var orderElement" + id + " in orderSource" + id + ")");
        body.OpenBlock();
        if (elementIsRef)
        {
            body.AppendLine("if (orderElement" + id + " is null) continue;");
        }

        body.AppendLine("var orderElementKey" + id + " = " + keyOf + "(orderElement" + id + ");");
        body.AppendLine("if ((object?)orderElementKey" + id + " is null)");
        body.OpenBlock();
        body.AppendLine(
            "(orderNull"
                + id
                + " ??= new global::System.Collections.Generic.List<"
                + elementType
                + ">()).Add(orderElement"
                + id
                + ");"
        );
        body.AppendLine("continue;");
        body.CloseBlock();
        body.AppendLine(
            "if (orderIndex"
                + id
                + ".TryGetValue(orderElementKey"
                + id
                + ", out var orderEntry"
                + id
                + "))"
        );
        body.OpenBlock();
        body.AppendLine(
            "orderIndex"
                + id
                + "[orderElementKey"
                + id
                + "] = (orderEntry"
                + id
                + ".element, orderEntry"
                + id
                + ".matches + 1);"
        );
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine(
            "orderIndex" + id + ".Add(orderElementKey" + id + ", (orderElement" + id + ", 1));"
        );
        body.CloseBlock();
        body.CloseBlock();
        body.AppendLine(
            "var orderMentioned"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(orderComparer"
                + id
                + ");"
        );
        body.AppendLine("var orderNullMentioned" + id + " = false;");
        body.AppendLine("foreach (var orderKey" + id + " in " + orderValue + ")");
        body.OpenBlock();
        body.AppendLine("var orderMatch" + id + " = default(" + elementType + ");");
        body.AppendLine("int orderMatches" + id + ";");
        body.AppendLine("if ((object?)orderKey" + id + " is null)");
        body.OpenBlock();
        body.AppendLine("orderMatches" + id + " = orderNull" + id + "?.Count ?? 0;");
        body.AppendLine(
            "if (orderMatches" + id + " == 1) orderMatch" + id + " = orderNull" + id + "![0];"
        );
        body.CloseBlock();
        body.AppendLine(
            "else if (orderIndex"
                + id
                + ".TryGetValue(orderKey"
                + id
                + ", out var orderEntry"
                + id
                + "))"
        );
        body.OpenBlock();
        body.AppendLine("orderMatches" + id + " = orderEntry" + id + ".matches;");
        body.AppendLine("orderMatch" + id + " = orderEntry" + id + ".element;");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("orderMatches" + id + " = 0;");
        body.CloseBlock();
        body.AppendLine("if (orderMatches" + id + " == 0)");
        body.OpenBlock();
        body.AppendLine("error = \"The order references an unknown key.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("if (orderMatches" + id + " > 1)");
        body.OpenBlock();
        body.AppendLine("error = \"The collection holds duplicate keys.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("if ((object?)orderKey" + id + " is null)");
        body.OpenBlock();
        body.AppendLine("orderNullMentioned" + id + " = true;");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("orderMentioned" + id + ".Add(orderKey" + id + ");");
        body.CloseBlock();
        body.AppendLine("reordered" + id + ".Add(orderMatch" + id + "!);");
        body.CloseBlock();
        body.AppendLine("foreach (var orderElement" + id + " in orderSource" + id + ")");
        body.OpenBlock();
        if (elementIsRef)
        {
            body.AppendLine("if (orderElement" + id + " is null) continue;");
        }

        body.AppendLine("var orderElementKey" + id + " = " + keyOf + "(orderElement" + id + ");");
        body.AppendLine(
            "var orderKnown"
                + id
                + " = (object?)orderElementKey"
                + id
                + " is null ? orderNullMentioned"
                + id
                + " : orderMentioned"
                + id
                + ".Contains(orderElementKey"
                + id
                + ");"
        );
        body.AppendLine(
            "if (!orderKnown" + id + ") reordered" + id + ".Add(orderElement" + id + ");"
        );
        body.CloseBlock();
        body.AppendLine(
            "builder."
                + esc
                + " = "
                + optional
                + ".Present("
                + slotValue.Replace("list" + id, "reordered" + id)
                + ");"
        );
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }
}
