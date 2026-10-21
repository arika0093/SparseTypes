using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Dictionary branches of the resolution fragment applier.</summary>
internal static class SparseResolutionDictEmitter
{
    internal static void AppendDictBranch(
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
        var valueValueType = ValueTypeOf(member);
        var valueIsModel = member.Collection.ValueType?.IsFragmentModel == true;
        var valueChangeSet = valueIsModel ? ValueChangeSetOf(member) : string.Empty;
        var valueFragment = valueIsModel ? ValueFragmentOf(member) : string.Empty;
        var id = member.Id;
        var segmentKind = runtime + "SparsePathSegmentKind";
        var dictType = "global::System.Collections.Generic.Dictionary<" + keyType + ", " + valueValueType + ">";
        var genericDictType = "global::System.Collections.Generic.IDictionary<" + keyType + ", " + valueValueType + ">";

        body.AppendLine("if (offset + 1 == path.Depth)");
        body.OpenBlock();
        SparseResolutionApplyEmitter.AppendCheckedSlot(body, "builder." + esc, optional, valueType, name, id, rejectNull: false);
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (offset + 2 > path.Depth)");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution path is too short for member '" + name + "'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var dictSegment" + id + " = path.Segments[offset + 1];");
        body.AppendLine("var rawDictKey" + id + " = dictSegment" + id + ".Key;");
        body.AppendLine(
            "if (dictSegment"
                + id
                + ".Kind != "
                + segmentKind
                + ".Key || rawDictKey"
                + id
                + " is null || !typeof("
                + keyType
                + ").IsInstanceOfType(rawDictKey"
                + id
                + "))"
        );
        body.OpenBlock();
        body.AppendLine("error = \"The key segment for member '" + name + "' has an unexpected type.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var dictKey" + id + " = (" + keyType + ")rawDictKey" + id + ";");
        if (!valueIsModel)
        {
            body.AppendLine("if (offset + 2 != path.Depth)");
            body.OpenBlock();
            body.AppendLine("error = \"Member '" + name + "' has no resolvable entry leaves.\";");
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
        AppendDictAbsentAdd(body, esc, optional, dictType, keyType, valueValueType, valueFragment, valueIsModel, id, name, runtime);
        body.CloseBlock();
        body.AppendLine(dictType + "? editable" + id + " = null;");
        if (workspace is not null)
        {
            // Detached staging: the built dictionary is reused while the slot
            // still references the published buffer; membership changes re-stage.
            body.AppendLine("var stageKey" + id + " = " + workspace + ".StagingKey(path, offset + 1);");
            body.AppendLine(
                "if (workspace.TryGetStaged(stageKey"
                    + id
                    + ", out var staged"
                    + id
                    + ") && global::System.Object.ReferenceEquals(builder."
                    + esc
                    + ".Value, staged"
                    + id
                    + "))"
            );
            body.OpenBlock();
            body.AppendLine("editable" + id + " = (" + dictType + ")staged" + id + "!;");
            body.CloseBlock();
            body.AppendLine("if (editable" + id + " is null)");
            body.OpenBlock();
        }

        body.AppendLine("if (builder." + esc + ".Value is " + dictType + " concrete" + id + ")");
        body.OpenBlock();
        body.AppendLine("editable" + id + " = new " + dictType + "(concrete" + id + ", concrete" + id + ".Comparer);");
        if (workspace is not null)
        {
            body.AppendLine("workspace.StoreStaged(stageKey" + id + ", editable" + id + ");");
        }

        body.CloseBlock();
        body.AppendLine("else if (builder." + esc + ".Value is " + genericDictType + " generic" + id + ")");
        body.OpenBlock();
        body.AppendLine(
            "editable"
                + id
                + " = new "
                + dictType
                + "(generic"
                + id
                + ", global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default);"
        );
        if (workspace is not null)
        {
            body.AppendLine("workspace.StoreStaged(stageKey" + id + ", editable" + id + ");");
        }

        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' has an unsupported dictionary shape.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        if (workspace is not null)
        {
            body.CloseBlock();
        }
        // Typed lookup honors the dictionary comparer; no display-text
        // fallback.
        body.AppendLine("var found" + id + " = editable" + id + ".TryGetValue(dictKey" + id + ", out _);");
        if (valueIsModel)
        {
            AppendDictStructuralEntry(
                body,
                esc,
                optional,
                name,
                keyType,
                valueValueType,
                valueFragment,
                valueChangeSet,
                id,
                member.Collection.ValueType?.IsReferenceType == true
                    || member.Collection.ValueType?.Name.EndsWith("?", StringComparison.Ordinal) == true,
                runtime,
                workspace
            );
        }
        else
        {
            var valueTypeModel = member.Collection.ValueType;
            var rejectNull = valueTypeModel is { IsReferenceType: false } scalarValue && !scalarValue.Name.EndsWith("?", StringComparison.Ordinal);
            AppendDictScalarEntry(body, esc, optional, name, valueValueType, id, rejectNull);
        }
    }

    private static void AppendDictAbsentAdd(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string dictType,
        string keyType,
        string valueValueType,
        string valueFragment,
        bool valueIsModel,
        int id,
        string name,
        string runtime
    )
    {
        var checkValue = SparseResolutionApplyEmitter.NonNullable(valueValueType);
        body.AppendLine("if (desired.Value is " + checkValue + " absentValue" + id + ")");
        body.OpenBlock();
        body.AppendLine("builder." + esc + " = " + optional + ".Present(new " + dictType + " { [dictKey" + id + "] = absentValue" + id + " });");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        if (valueIsModel)
        {
            body.AppendLine("if (desired.Value is " + valueFragment + " absentFragment" + id + ")");
            body.OpenBlock();
            body.AppendLine(
                "builder." + esc + " = " + optional + ".Present(new " + dictType + " { [dictKey" + id + "] = absentFragment" + id + ".ToModel() });"
            );
            body.AppendLine("updated = builder.Build();");
            body.AppendLine("return true;");
            body.CloseBlock();
            var valueModel = valueFragment.EndsWith(".Fragment", System.StringComparison.Ordinal)
                ? valueFragment.Substring(0, valueFragment.Length - ".Fragment".Length)
                : valueValueType;
            body.AppendLine("if (desired.Value is " + valueModel + " absentModel" + id + ")");
            body.OpenBlock();
            body.AppendLine("builder." + esc + " = " + optional + ".Present(new " + dictType + " { [dictKey" + id + "] = absentModel" + id + " });");
            body.AppendLine("updated = builder.Build();");
            body.AppendLine("return true;");
            body.CloseBlock();
        }

        body.AppendLine("error = \"Member '" + name + "' is absent and the resolution value cannot seed it.\";");
        body.AppendLine("return false;");
        _ = runtime;
        _ = keyType;
    }

    private static void AppendDictScalarEntry(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string valueValueType,
        int id,
        bool rejectNull
    )
    {
        var checkValue = SparseResolutionApplyEmitter.NonNullable(valueValueType);
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("if (found" + id + ") editable" + id + ".Remove(dictKey" + id + ");");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is null)");
        body.OpenBlock();
        if (rejectNull)
        {
            body.AppendLine("error = \"Member '" + name + "' does not accept null values.\";");
            body.AppendLine("return false;");
        }
        else
        {
            body.AppendLine("editable" + id + "[dictKey" + id + "] = default(" + valueValueType + ")!;");
        }
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + checkValue + " entry" + id + ")");
        body.OpenBlock();
        body.AppendLine("editable" + id + "[dictKey" + id + "] = entry" + id + ";");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution value for member '" + name + "' has an unsupported type.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }

    private static void AppendDictStructuralEntry(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string keyType,
        string valueValueType,
        string valueFragment,
        string valueChangeSet,
        int id,
        bool valueMayBeNull,
        string runtime,
        string? workspace
    )
    {
        _ = runtime;
        _ = keyType;
        var valueModel = valueFragment.EndsWith(".Fragment", System.StringComparison.Ordinal)
            ? valueFragment.Substring(0, valueFragment.Length - ".Fragment".Length)
            : valueValueType;
        body.AppendLine("if (offset + 2 == path.Depth)");
        body.OpenBlock();
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("if (found" + id + ") editable" + id + ".Remove(dictKey" + id + ");");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is null)");
        body.OpenBlock();
        body.AppendLine("error = \"Null entries are not supported for member '" + name + "'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + valueValueType + " structEntry" + id + ")");
        body.OpenBlock();
        body.AppendLine("editable" + id + "[dictKey" + id + "] = structEntry" + id + ";");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + valueFragment + " structFragment" + id + ")");
        body.OpenBlock();
        body.AppendLine("editable" + id + "[dictKey" + id + "] = structFragment" + id + ".ToModel();");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + valueModel + " structModel" + id + ")");
        body.OpenBlock();
        body.AppendLine("editable" + id + "[dictKey" + id + "] = structModel" + id + ";");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution value for member '" + name + "' has an unsupported type.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (!found" + id + ")");
        body.OpenBlock();
        body.AppendLine("error = \"No entry with the key was found in member '" + name + "'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        if (valueMayBeNull)
        {
            body.AppendLine("if (editable" + id + "[dictKey" + id + "] is null)");
            body.OpenBlock();
            body.AppendLine("error = \"The entry for member '" + name + "' is missing.\";");
            body.AppendLine("return false;");
            body.CloseBlock();
        }
        body.AppendLine("var valueFragment" + id + " = " + valueFragment + ".From(editable" + id + "[dictKey" + id + "]);");
        body.AppendLine(
            "if (!"
                + valueChangeSet
                + ".__SparseTryApplyResolution(valueFragment"
                + id
                + ", path, offset + 2, desired"
                + SparseResolutionApplyEmitter.WorkspaceArgument(workspace)
                + ", out var updatedValue"
                + id
                + ", out error)) return false;"
        );
        body.AppendLine("if (updatedValue" + id + " is null)");
        body.OpenBlock();
        body.AppendLine("error = \"The entry resolution produced no fragment.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("editable" + id + "[dictKey" + id + "] = updatedValue" + id + ".ToModel();");
        body.AppendLine("builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }
}
