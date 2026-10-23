using System;
using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetPayloadItemEmitter
{
    internal static void AppendPayloadItemHelper(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string payloadContainerName,
        string? modelType
    )
    {
        var id = member.Id;
        var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var valueCs = isKeyed ? SparseChangeSetBasicsEmitter.ElementChangeSetOf(member) : SparseChangeSetBasicsEmitter.ValueChangeSetOf(member);
        var valueFrag = isKeyed ? SparseChangeSetBasicsEmitter.ElementFragmentOf(member) : SparseChangeSetBasicsEmitter.ValueFragmentOf(member);
        var itemValueType = isKeyed ? SparseChangeSetBasicsEmitter.ElementTypeOf(member) : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
        var itemValueIsReference = isKeyed ? member.Collection.ElementType.IsReferenceType : member.Collection.ValueType?.IsReferenceType == true;
        var hasEdit = isKeyed || member.Collection.ValueType?.IsFragmentModel == true;
        var isModelValue = isKeyed ? member.Collection.ElementType.IsFragmentModel : member.Collection.ValueType?.IsFragmentModel == true;
        var itemType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(payloadContainerName, modelType, "Item", id);
        var restoredEditPrefix = isModelValue ? "restoredEdit ?? " : string.Empty;
        if (isModelValue)
        {
            code.AppendLine(
                "internal static "
                    + trans
                    + ".Item __SparsePayloadItem"
                    + id
                    + "("
                    + itemType
                    + " item) => __SparsePayloadItem"
                    + id
                    + "(item, null);"
            );
        }
        // Internal so the payload baseline-free projection reuses the same
        // validation and conversion instead of duplicating it.
        code.AppendLine(
            "internal static "
                + trans
                + ".Item __SparsePayloadItem"
                + id
                + "("
                + itemType
                + " item"
                + (isModelValue ? ", " + valueCs + "? restoredEdit" : string.Empty)
                + ")"
        );
        code.OpenBlock();
        code.AppendLine("if (item is null) throw new global::System.ArgumentException(\"Payload item is required.\");");
        code.AppendLine(
            "if (item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Add && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Remove && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Edit && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Reorder) throw new global::System.ArgumentException(\"Unsupported payload item kind.\");"
        );
        if (!isKeyed)
        {
            code.AppendLine(
                "if (item.Kind == "
                    + runtime
                    + "ChangePayloadItemKind.Reorder) throw new global::System.ArgumentException(\"Reorder is not supported for dictionary payload items.\");"
            );
        }
        // Redacted item histories only support the baseline-discarding
        // projection.
        code.AppendLine(
            "if (item.Before is not null && item.Before.State == "
                + runtime
                + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted payload cannot convert to a ChangeSet. Project it with ToPatch instead.\");"
        );
        code.AppendLine(
            "if (item.After is not null && item.After.State == "
                + runtime
                + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\");"
        );
        code.AppendLine("var before = item.Before is null ? " + runtime + "Optional<" + itemValueType + ">.Missing : item.Before.ToOptional();");
        code.AppendLine("var after = item.After is null ? " + runtime + "Optional<" + itemValueType + ">.Missing : item.After.ToOptional();");
        var added = "item.Kind == " + runtime + "ChangePayloadItemKind.Add";
        var removed = "item.Kind == " + runtime + "ChangePayloadItemKind.Remove";
        var edited = "item.Kind == " + runtime + "ChangePayloadItemKind.Edit";
        // Kind/endpooint consistency: reject invalid payloads before producing
        // an unsafe ChangeSet.
        code.AppendLine("if (" + added + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (before.IsPresent || !after.IsPresent) throw new global::System.ArgumentException(\"An added payload item must omit 'before' and contain 'after'.\");"
        );
        if (isModelValue)
        {
            code.AppendLine(
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"An added payload item must not contain 'edit'.\");"
            );
        }

        if (isKeyed)
        {
            code.AppendLine(
                "if (item.BeforeIndex != -1 || item.AfterIndex < 0 || item.IsReordered) throw new global::System.ArgumentException(\"An added keyed payload item has invalid indices.\");"
            );
        }

        code.CloseBlock();
        code.AppendLine("else if (" + removed + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (!before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"A removed payload item must contain 'before' and omit 'after'.\");"
        );
        if (isModelValue)
        {
            code.AppendLine(
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"A removed payload item must not contain 'edit'.\");"
            );
        }

        if (isKeyed)
        {
            code.AppendLine(
                "if (item.BeforeIndex < 0 || item.AfterIndex != -1 || item.IsReordered) throw new global::System.ArgumentException(\"A removed keyed payload item has invalid indices.\");"
            );
        }

        code.CloseBlock();
        code.AppendLine("else if (" + edited + ")");
        code.OpenBlock();
        if (isModelValue)
        {
            code.AppendLine(
                "if (item.Edit is null ? !before.IsPresent || !after.IsPresent : before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"A model item update requires only 'edit'; a replacement requires both 'before' and 'after'.\");"
            );
            if (isKeyed)
            {
                code.AppendLine(
                    "if (item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"An updated or replaced keyed payload item has invalid indices.\");"
                );
            }
        }
        else
        {
            code.AppendLine(
                "if (!before.IsPresent || !after.IsPresent) throw new global::System.ArgumentException(\"An edited scalar payload item must contain 'before' and 'after'.\");"
            );
            if (isKeyed)
            {
                code.AppendLine(
                    "if (item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"An edited keyed payload item has invalid indices.\");"
                );
            }
        }
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // Reorder (keyed only; dictionary reorder rejected above).
        code.AppendLine(
            "if (before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"A reordered payload item must omit 'before' and 'after'.\");"
        );
        if (isModelValue)
        {
            code.AppendLine(
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"A reordered payload item must not contain 'edit'.\");"
            );
        }

        if (isKeyed)
        {
            code.AppendLine(
                "if (!item.IsReordered || item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"A reordered keyed payload item has invalid indices.\");"
            );
        }

        code.CloseBlock();
        var beforeIsPresent = itemValueIsReference ? "before.IsPresent && before.Value is not null" : "before.IsPresent";
        var afterIsPresent = itemValueIsReference ? "after.IsPresent && after.Value is not null" : "after.IsPresent";
        var beforeValue = "before.Value" + (itemValueIsReference ? "!" : string.Empty);
        var afterValue = "after.Value" + (itemValueIsReference ? "!" : string.Empty);
        if (isKeyed)
        {
            var beforeFragment =
                "("
                + beforeIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + beforeValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + afterIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + afterValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var derivedEdit = valueCs + ".Between(" + beforeFragment + ", " + afterFragment + ")";
            var edit = hasEdit
                ? "("
                    + edited
                    + " && item.Edit is not null"
                    + " ? "
                    + restoredEditPrefix
                    + "item.Edit?.ToChangeSetCore() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
                    + derivedEdit
                    + ")"
                : derivedEdit;
            code.AppendLine(
                "return new "
                    + trans
                    + ".Item(item.Key, "
                    + (
                        SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                            ? "(" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "item.Key") + " ? item.TemporaryKey : null)"
                            : "null"
                    )
                    + ", before, after, item.BeforeIndex, item.AfterIndex, "
                    + added
                    + ", "
                    + removed
                    + ", "
                    + edited
                    + ", item.IsReordered, "
                    + edit
                    + ", false);"
            );
        }
        else if (hasEdit)
        {
            var beforeFragment =
                "("
                + beforeIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + beforeValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + afterIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + afterValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var derivedEdit = valueCs + ".Between(" + beforeFragment + ", " + afterFragment + ")";
            code.AppendLine(
                "return new "
                    + trans
                    + ".Item(item.Key, before, after, "
                    + added
                    + ", "
                    + removed
                    + ", "
                    + edited
                    + ", "
                    + "("
                    + edited
                    + " && item.Edit is not null"
                    + " ? restoredEdit ?? item.Edit?.ToChangeSetCore() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
                    + derivedEdit
                    + "), false);"
            );
        }
        else
        {
            code.AppendLine("return new " + trans + ".Item(item.Key, before, after, " + added + ", " + removed + ", " + edited + ", false);");
        }
        code.CloseBlock();
    }

    internal static void AppendMemberPayload(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string endpoint,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace = null
    )
    {
        var id = member.Id;
        var payloadChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Change");
        var isCollection = SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member);
        var memberValueType = SparseChangeSetBasicsEmitter.IsNested(member)
            ? SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                dialect,
                member.ChildModel!.Value.NonNullableName,
                "Root",
                implementationNamespace
            ) + "?"
            : SparseChangeSetBasicsEmitter.FragmentValueType(member);
        var payloadBase = isCollection
            ? SparseChangeSetPayloadEmitter.PayloadName(modelType, "CollectionChange")
                + "<"
                + memberValueType
                + ", "
                + SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Item", id)
                + ">"
            : payloadChange;
        code.AppendLine("/// <summary>Payload change for one member.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public sealed class " + SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Change", id) + " : " + payloadBase);
        code.OpenBlock();
        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLine("/// <summary>Gets or sets the snapshot value.</summary>");
        SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
        SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "Value", 0, dialect.EmitJson);
        if (dialect.EmitJson)
        {
            SparseChangePayloadJsonBridgeEmitter.AppendEndpointConverterAttribute(code, memberValueType, implementationNamespace);
        }

        AppendHiddenMemberAttribute(code);
        code.AppendLine("public " + endpoint + "<" + memberValueType + ">? Value { get; set; }");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childModelType = member.ChildModel!.Value.NonNullableName;
            var childPayload = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(dialect, childModelType, "Core", implementationNamespace);
            code.AppendLine("/// <summary>Gets or sets the nested change core.</summary>");
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "Nested", 1, dialect.EmitJson);
            AppendHiddenMemberAttribute(code);
            code.AppendLine("public " + childPayload + "? Nested { get; set; }");
        }
        else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLine("/// <summary>Gets or sets the before key order.</summary>");
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "BeforeOrder", 5, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public global::System.Collections.Generic.List<" + keyType + ">? BeforeOrder { get; set; }");
                code.AppendLine("/// <summary>Gets or sets the after key order.</summary>");
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "AfterOrder", 6, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public global::System.Collections.Generic.List<" + keyType + ">? AfterOrder { get; set; }");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("/// <summary>Gets or sets the before temporary order.</summary>");
                    SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                    SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "TempBeforeOrder", 8, dialect.EmitJson);
                    AppendHiddenMemberAttribute(code);
                    code.AppendLine("public global::System.Collections.Generic.List<global::System.Guid>? TempBeforeOrder { get; set; }");
                    code.AppendLine("/// <summary>Gets or sets the after temporary order.</summary>");
                    SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                    SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "TempAfterOrder", 9, dialect.EmitJson);
                    AppendHiddenMemberAttribute(code);
                    code.AppendLine("public global::System.Collections.Generic.List<global::System.Guid>? TempAfterOrder { get; set; }");
                }
            }
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLine("/// <summary>Gets or sets the before endpoint.</summary>");
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "Before", 0, dialect.EmitJson);
            if (dialect.EmitJson)
            {
                SparseChangePayloadJsonBridgeEmitter.AppendEndpointConverterAttribute(code, valueType, implementationNamespace);
            }

            AppendHiddenMemberAttribute(code);
            code.AppendLine("public " + endpoint + "<" + valueType + ">? Before { get; set; }");
            code.AppendLine("/// <summary>Gets or sets the after endpoint.</summary>");
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "After", 1, dialect.EmitJson);
            if (dialect.EmitJson)
            {
                SparseChangePayloadJsonBridgeEmitter.AppendEndpointConverterAttribute(code, valueType, implementationNamespace);
            }

            AppendHiddenMemberAttribute(code);
            code.AppendLine("public " + endpoint + "<" + valueType + ">? After { get; set; }");
        }
        code.CloseBlock();

        if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            var itemValueType = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
            var isModelValue = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? member.Collection.ElementType.IsFragmentModel
                : member.Collection.ValueType?.IsFragmentModel == true;
            var childName = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    dialect,
                    member.Collection.ElementType.NonNullableName,
                    "Core",
                    implementationNamespace
                )
                : SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    dialect,
                    member.Collection.ValueType!.Value.NonNullableName,
                    "Core",
                    implementationNamespace
                );
            var itemOperationBase =
                SparseChangeSetPayloadEmitter.PayloadName(modelType, "ItemOperation") + "<" + keyType + ", " + itemValueType + ">";
            code.AppendLine("/// <summary>Per-item change for a collection member.</summary>");
            code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            code.AppendLine(
                "public sealed class " + SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Item", id) + " : " + itemOperationBase
            );
            code.OpenBlock();
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                code.AppendLine("/// <summary>Gets or sets the before index.</summary>");
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "BeforeIndex", 2, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public int BeforeIndex { get; set; } = -1;");
                code.AppendLine("/// <summary>Gets or sets the after index.</summary>");
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "AfterIndex", 3, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public int AfterIndex { get; set; } = -1;");
                code.AppendLine("/// <summary>Gets or sets whether the item was reordered.</summary>");
                SparseChangeSetPayloadEmitter.AppendIgnoreDefault(code, dialect.EmitJson);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "IsReordered", 6, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public bool IsReordered { get; set; }");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("/// <summary>Gets or sets the temporary identity for unassigned items.</summary>");
                    SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                    SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "TemporaryKey", 8, dialect.EmitJson);
                    AppendHiddenMemberAttribute(code);
                    code.AppendLine("public global::System.Guid? TemporaryKey { get; set; }");
                }
            }
            if (isModelValue)
            {
                code.AppendLine("/// <summary>Gets or sets the nested edit core.</summary>");
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, dialect.EmitJson);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, "Edit", 7, dialect.EmitJson);
                AppendHiddenMemberAttribute(code);
                code.AppendLine("public " + childName + "? Edit { get; set; }");
            }
            code.CloseBlock();
        }
    }

    private static void AppendHiddenMemberAttribute(SharedIndentedBuilder code) =>
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
}
