namespace SparseTypes.Generator.Shared;

/// <summary>Emits per-member payload JSON read and write bodies.</summary>
internal static class SparsePayloadJsonMemberEmitter
{
    /// <summary>Emits one member change codec.</summary>
    internal static void AppendMember(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context,
        SparseMemberModel member
    )
    {
        var memberChange = context.MemberDto("Change", member.Id);
        var isNested = SparseChangeSetBasicsEmitter.IsNested(member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var isDict = SparseChangeSetBasicsEmitter.IsDict(member);
        code.AppendLineAt(
            2,
            "private static object ReadMember" + member.Id + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");");
        code.AppendLineAt(3, "var change = new " + memberChange + "();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLineAt(4, "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed change.\");");
        code.AppendLineAt(4, "var name = reader.GetString() ?? string.Empty;");
        code.AppendLineAt(4, "if (name == \"member\") { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\"); reader.Skip(); continue; }");
        code.AppendLineAt(4, "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\");");
        if (isNested)
        {
            if (member.ChildModel is not { } child)
                throw new global::System.InvalidOperationException("Nested member is missing its child model.");
            var childType = child.NonNullableName;
            code.AppendLineAt(4, "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadChildEndpoint" + member.Id + "(ref reader, options); }");
            code.AppendLineAt(
                4,
                "else if (name == \"nested\") { if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) change.Nested = null; else change.Nested = " + childType + ".PayloadJsonConverter.ReadCore(ref reader, options); }"
            );
            code.AppendLineAt(4, "else reader.Skip();");
        }
        else if (isKeyed || isDict)
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLineAt(4, "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else if (name == \"items\") change.Items = ReadItems" + member.Id + "(ref reader, options);");
            if (isKeyed)
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLineAt(4, "else if (name == \"beforeOrder\") change.BeforeOrder = ReadKeyList<" + keyType + ">(ref reader, options);");
                code.AppendLineAt(4, "else if (name == \"afterOrder\") change.AfterOrder = ReadKeyList<" + keyType + ">(ref reader, options);");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLineAt(4, "else if (name == \"tempBeforeOrder\") change.TempBeforeOrder = ReadGuidList(ref reader);");
                    code.AppendLineAt(4, "else if (name == \"tempAfterOrder\") change.TempAfterOrder = ReadGuidList(ref reader);");
                }
            }

            code.AppendLineAt(4, "else reader.Skip();");
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLineAt(4, "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<" + valueType + ">(ref reader, options, null, null); }");
            code.AppendLineAt(4, "else reader.Skip();");
        }

        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return change;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteMember" + member.Id + "Body(global::System.Text.Json.Utf8JsonWriter writer, " + memberChange + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        if (isNested)
        {
            if (member.ChildModel is not { } writeChild)
                throw new global::System.InvalidOperationException("Nested member is missing its child model.");
            var writeChildModel = writeChild.NonNullableName;
            code.AppendLineAt(
                3,
                "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteChildEndpoint" + member.Id + "(writer, change.Value, options); }"
            );
            code.AppendLineAt(
                3,
                "if (change.Nested is not null) { writer.WritePropertyName(\"nested\"); " + writeChildModel + ".PayloadJsonConverter.WriteCore(writer, change.Nested, options); }"
            );
        }
        else if (isKeyed || isDict)
        {
            code.AppendLineAt(3, "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteEndpoint(writer, change.Value, options, null); }");
            code.AppendLineAt(3, "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options, null); }");
            code.AppendLineAt(3, "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options, null); }");
            code.AppendLineAt(3, "writer.WritePropertyName(\"items\"); WriteItems" + member.Id + "(writer, change.Items, options);");
            if (isKeyed)
            {
                var writeKeyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLineAt(3, "if (change.BeforeOrder is not null) { writer.WritePropertyName(\"beforeOrder\"); WriteKeyList<" + writeKeyType + ">(writer, change.BeforeOrder, options); }");
                code.AppendLineAt(3, "if (change.AfterOrder is not null) { writer.WritePropertyName(\"afterOrder\"); WriteKeyList<" + writeKeyType + ">(writer, change.AfterOrder, options); }");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLineAt(3, "if (change.TempBeforeOrder is not null) { writer.WritePropertyName(\"tempBeforeOrder\"); WriteGuidList(writer, change.TempBeforeOrder); }");
                    code.AppendLineAt(3, "if (change.TempAfterOrder is not null) { writer.WritePropertyName(\"tempAfterOrder\"); WriteGuidList(writer, change.TempAfterOrder); }");
                }
            }
        }
        else
        {
            code.AppendLineAt(3, "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteEndpoint(writer, change.Value, options, null); }");
            code.AppendLineAt(3, "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options, null); }");
            code.AppendLineAt(3, "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options, null); }");
        }

        code.AppendLineAt(2, "}");
        code.AppendLine();
        if (isNested)
            AppendChildEndpoint(code, context, member);
        if (isKeyed || isDict)
            AppendItems(code, context, member);
    }

    private static void AppendChildEndpoint(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context,
        SparseMemberModel member
    )
    {
        if (member.ChildModel is not { } child)
            throw new global::System.InvalidOperationException("Nested member is missing its child model.");
        var childType = child.NonNullableName;
        var childRoot = context.ChildDto(childType, "Root");
        var endpoint = context.Runtime + "ChangePayloadEndpoint<" + childRoot + "?";
        var endpointFull = endpoint + ">";
        code.AppendLineAt(2, "private static " + endpointFull + "? ReadChildEndpoint" + member.Id + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLineAt(3, "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");");
        code.AppendLineAt(3, "var state = default(" + context.Runtime + "ChangePayloadState);");
        code.AppendLineAt(3, "var hasState = false;");
        code.AppendLineAt(3, childRoot + "? value = default;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLineAt(4, "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        code.AppendLineAt(4, "if (reader.ValueTextEquals(\"state\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); state = ParseState(reader.GetString()); hasState = true; }");
        code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"value\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null) value = " + childType + ".PayloadJsonConverter.ReadRoot(ref reader, options); }");
        code.AppendLineAt(4, "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); reader.Skip(); }");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");");
        code.AppendLineAt(3, "return new " + context.Runtime + "ChangePayloadEndpoint<" + childRoot + "?> { State = state, Value = value };");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(2, "private static void WriteChildEndpoint" + member.Id + "(global::System.Text.Json.Utf8JsonWriter writer, " + endpointFull + "? value, global::System.Text.Json.JsonSerializerOptions options)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WriteString(\"state\", " + context.Runtime + "ChangePayloadWire.ToToken(value.State));");
        code.AppendLineAt(3, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(3, "if (value.Value is null) writer.WriteNullValue(); else " + childType + ".PayloadJsonConverter.WriteRoot(writer, value.Value, options);");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendItems(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context,
        SparseMemberModel member
    )
    {
        var itemType = context.MemberDto("Item", member.Id);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var itemValueType = isKeyed
            ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
            : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
        var isModelValue = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        var hasTempKey = isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        code.AppendLineAt(
            2,
            "private static global::System.Collections.Generic.List<" + itemType + "> ReadItems" + member.Id + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return new();");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload items must be an array.\");"
        );
        code.AppendLineAt(3, "var list = new global::System.Collections.Generic.List<" + itemType + ">();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;");
        code.AppendLineAt(4, "list.Add(ReadItem" + member.Id + "(ref reader, options));");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return list;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static " + itemType + " ReadItem" + member.Id + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A payload item must be an object.\");"
        );
        code.AppendLineAt(3, "var item = new " + itemType + "();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\");"
        );
        code.AppendLineAt(4, "if (reader.ValueTextEquals(\"key\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.Key = ReadKey<" + keyType + ">(ref reader, options); }");
        code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"kind\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.Kind = ParseKind(reader.GetString()); }");
        if (isKeyed)
        {
            code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"beforeIndex\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.BeforeIndex = reader.GetInt32(); }");
            code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"afterIndex\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.AfterIndex = reader.GetInt32(); }");
            code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"isReordered\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.IsReordered = reader.TokenType != global::System.Text.Json.JsonTokenType.Null && reader.GetBoolean(); }");
        }

        code.AppendLineAt(
            4,
            "else if (reader.ValueTextEquals(\"before\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (!TryReadNull(ref reader)) item.Before = ReadEndpoint<" + itemValueType + ">(ref reader, options, null, null); }"
        );
        code.AppendLineAt(
            4,
            "else if (reader.ValueTextEquals(\"after\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (!TryReadNull(ref reader)) item.After = ReadEndpoint<" + itemValueType + ">(ref reader, options, null, null); }"
        );
        if (isModelValue)
        {
            if (member.Collection.ValueType is null && !isKeyed)
                throw new global::System.InvalidOperationException("Dictionary item is missing its value model.");
            var editChild = isKeyed
                ? member.Collection.ElementType.NonNullableName
                : member.Collection.ValueType.GetValueOrDefault().NonNullableName;
            var editCore = context.ChildDto(editChild, "Core");
            _ = editCore;
            code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"edit\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) item.Edit = null; else item.Edit = " + editChild + ".PayloadJsonConverter.ReadCore(ref reader, options); }");
        }
        else
        {
            code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"edit\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); reader.Skip(); }");
        }

        if (hasTempKey)
        {
            code.AppendLineAt(
                4,
                "else if (reader.ValueTextEquals(\"temporaryKey\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.TemporaryKey = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : (global::System.Guid?)reader.GetGuid(); }"
            );
        }
        code.AppendLineAt(4, "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); reader.Skip(); }");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return item;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteItems" + member.Id + "(global::System.Text.Json.Utf8JsonWriter writer, global::System.Collections.Generic.List<" + itemType + "> items, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartArray();");
        code.AppendLineAt(3, "foreach (var item in items)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "writer.WritePropertyName(\"key\"); WriteKey(writer, item.Key, options);");
        code.AppendLineAt(4, "writer.WriteString(\"kind\", " + context.Runtime + "ChangePayloadWire.ToToken(item.Kind));");
        if (isKeyed)
        {
            code.AppendLineAt(4, "writer.WriteNumber(\"beforeIndex\", item.BeforeIndex);");
            code.AppendLineAt(4, "writer.WriteNumber(\"afterIndex\", item.AfterIndex);");
            code.AppendLineAt(4, "if (item.IsReordered) writer.WriteBoolean(\"isReordered\", true);");
        }

        code.AppendLineAt(4, "if (item.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, item.Before, options, null); }");
        code.AppendLineAt(4, "if (item.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, item.After, options, null); }");
        if (isModelValue)
        {
            if (member.Collection.ValueType is null && !isKeyed)
                throw new global::System.InvalidOperationException("Dictionary item is missing its value model.");
            var writeEditChild = isKeyed
                ? member.Collection.ElementType.NonNullableName
                : member.Collection.ValueType.GetValueOrDefault().NonNullableName;
            code.AppendLineAt(4, "if (item.Edit is not null) { writer.WritePropertyName(\"edit\"); " + writeEditChild + ".PayloadJsonConverter.WriteCore(writer, item.Edit, options); }");
        }

        if (hasTempKey)
        {
            code.AppendLineAt(4, "if (item.TemporaryKey is not null) { writer.WritePropertyName(\"temporaryKey\"); writer.WriteStringValue(item.TemporaryKey.Value.ToString()); }");
        }
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "writer.WriteEndArray();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }
}
