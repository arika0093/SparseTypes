namespace SparseTypes.Generator.Shared;

/// <summary>Emits per-member payload JSON read and write bodies.</summary>
internal static class SparsePayloadJsonMemberEmitter
{
    /// <summary>Emits one member change codec.</summary>
    internal static void AppendMember(SharedIndentedBuilder code, PayloadJsonEmissionContext context, SparseMemberModel member)
    {
        var memberChange = context.MemberDto("Change", member.Id);
        var changeType = context.Dto("Change");
        var isNested = SparseChangeSetBasicsEmitter.IsNested(member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var isDict = SparseChangeSetBasicsEmitter.IsDict(member);
        var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
        SparseJsonCollectionModel? valueCollection = null;
        var isValueCollection = false;
        if (!isNested)
        {
            isValueCollection = SparseJsonCollectionShapes.TryResolve(member, valueType, out valueCollection);
            isValueCollection = isValueCollection && valueCollection is not null;
        }
        var readCodec = isValueCollection ? ", ReadCollection" + member.Id + ", WriteCollection" + member.Id : ", null, null";
        var writeCodec = isValueCollection ? ", WriteCollection" + member.Id : ", null";
        code.AppendLine(
            "private static "
                + changeType
                + " ReadMember"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");"
        );
        code.AppendLine("var change = new " + memberChange + "();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed change.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine(
            "if (name == \"member\") { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\"); reader.Skip(); continue; }"
        );
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\");");
        if (isNested)
        {
            if (member.ChildModel is not { } child)
            {
                throw new global::System.InvalidOperationException("Nested member is missing its child model.");
            }

            var childType = child.NonNullableName;
            code.AppendLine(
                "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadChildEndpoint" + member.Id + "(ref reader, options); }"
            );
            code.AppendLine(
                "else if (name == \"nested\") { if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) change.Nested = null; else change.Nested = "
                    + childType
                    + ".PayloadJsonConverter.ReadCore(ref reader, options); }"
            );
            code.AppendLine("else reader.Skip();");
        }
        else if (isKeyed || isDict)
        {
            code.AppendLine(
                "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine(
                "else if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine(
                "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine("else if (name == \"items\") change.Items = ReadItems" + member.Id + "(ref reader, options);");
            if (isKeyed)
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLine("else if (name == \"beforeOrder\") change.BeforeOrder = ReadKeyList<" + keyType + ">(ref reader, options);");
                code.AppendLine("else if (name == \"afterOrder\") change.AfterOrder = ReadKeyList<" + keyType + ">(ref reader, options);");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("else if (name == \"tempBeforeOrder\") change.TempBeforeOrder = ReadGuidList(ref reader);");
                    code.AppendLine("else if (name == \"tempAfterOrder\") change.TempAfterOrder = ReadGuidList(ref reader);");
                }
            }

            code.AppendLine("else reader.Skip();");
        }
        else
        {
            code.AppendLine(
                "if (name == \"value\") { if (!TryReadNull(ref reader)) change.Value = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine(
                "else if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine(
                "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<"
                    + valueType
                    + ">(ref reader, options"
                    + readCodec
                    + "); }"
            );
            code.AppendLine("else reader.Skip();");
        }

        code.CloseBlock();
        code.AppendLine("return change;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteMember"
                + member.Id
                + "Body(global::System.Text.Json.Utf8JsonWriter writer, "
                + memberChange
                + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        if (isNested)
        {
            if (member.ChildModel is not { } writeChild)
            {
                throw new global::System.InvalidOperationException("Nested member is missing its child model.");
            }

            var writeChildModel = writeChild.NonNullableName;
            code.AppendLine(
                "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteChildEndpoint"
                    + member.Id
                    + "(writer, change.Value, options); }"
            );
            code.AppendLine(
                "if (change.Nested is not null) { writer.WritePropertyName(\"nested\"); "
                    + writeChildModel
                    + ".PayloadJsonConverter.WriteCore(writer, change.Nested, options); }"
            );
        }
        else if (isKeyed || isDict)
        {
            code.AppendLine(
                "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteEndpoint(writer, change.Value, options"
                    + writeCodec
                    + "); }"
            );
            code.AppendLine(
                "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options"
                    + writeCodec
                    + "); }"
            );
            code.AppendLine(
                "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options"
                    + writeCodec
                    + "); }"
            );
            code.AppendLine("writer.WritePropertyName(\"items\"); WriteItems" + member.Id + "(writer, change.Items, options);");
            if (isKeyed)
            {
                var writeKeyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLine(
                    "if (change.BeforeOrder is not null) { writer.WritePropertyName(\"beforeOrder\"); WriteKeyList<"
                        + writeKeyType
                        + ">(writer, change.BeforeOrder, options); }"
                );
                code.AppendLine(
                    "if (change.AfterOrder is not null) { writer.WritePropertyName(\"afterOrder\"); WriteKeyList<"
                        + writeKeyType
                        + ">(writer, change.AfterOrder, options); }"
                );
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine(
                        "if (change.TempBeforeOrder is not null) { writer.WritePropertyName(\"tempBeforeOrder\"); WriteGuidList(writer, change.TempBeforeOrder); }"
                    );
                    code.AppendLine(
                        "if (change.TempAfterOrder is not null) { writer.WritePropertyName(\"tempAfterOrder\"); WriteGuidList(writer, change.TempAfterOrder); }"
                    );
                }
            }
        }
        else
        {
            code.AppendLine(
                "if (change.Value is not null) { writer.WritePropertyName(\"value\"); WriteEndpoint(writer, change.Value, options"
                    + writeCodec
                    + "); }"
            );
            code.AppendLine(
                "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options"
                    + writeCodec
                    + "); }"
            );
            code.AppendLine(
                "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options"
                    + writeCodec
                    + "); }"
            );
        }

        code.CloseBlock();
        code.AppendLine();
        if (isNested)
        {
            AppendChildEndpoint(code, context, member);
        }

        if (isValueCollection && valueCollection is not null)
        {
            SparseJsonCollectionShapes.AppendCollectionCodec(
                code,
                valueType,
                valueCollection,
                "ReadCollection" + member.Id,
                "WriteCollection" + member.Id,
                "GetValueInfo"
            );
        }

        if (isKeyed || isDict)
        {
            AppendItems(code, context, member);
        }
    }

    private static void AppendChildEndpoint(SharedIndentedBuilder code, PayloadJsonEmissionContext context, SparseMemberModel member)
    {
        if (member.ChildModel is not { } child)
        {
            throw new global::System.InvalidOperationException("Nested member is missing its child model.");
        }

        var childType = child.NonNullableName;
        var childRoot = context.ChildDto(childType, "Root");
        var endpoint = context.Runtime + "ChangePayloadEndpoint<" + childRoot + "?";
        var endpointFull = endpoint + ">";
        code.AppendLine(
            "private static "
                + endpointFull
                + "? ReadChildEndpoint"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLine("var state = default(" + context.Runtime + "ChangePayloadState);");
        code.AppendLine("var hasState = false;");
        code.AppendLine(childRoot + "? value = default;");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLine(
            "if (reader.ValueTextEquals(\"state\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); state = ParseState(reader.GetString()); hasState = true; }"
        );
        code.AppendLine(
            "else if (reader.ValueTextEquals(\"value\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null) value = "
                + childType
                + ".PayloadJsonConverter.ReadRoot(ref reader, options); }"
        );
        code.AppendLine("else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); reader.Skip(); }");
        code.CloseBlock();
        code.AppendLine("if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");");
        code.AppendLine("return new " + context.Runtime + "ChangePayloadEndpoint<" + childRoot + "?> { State = state, Value = value };");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteChildEndpoint"
                + member.Id
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + endpointFull
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WriteString(\"state\", " + context.Runtime + "ChangePayloadWire.ToToken(value.State));");
        code.AppendLine("writer.WritePropertyName(\"value\");");
        code.AppendLine(
            "if (value.Value is null) writer.WriteNullValue(); else " + childType + ".PayloadJsonConverter.WriteRoot(writer, value.Value, options);"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendItems(SharedIndentedBuilder code, PayloadJsonEmissionContext context, SparseMemberModel member)
    {
        var itemType = context.MemberDto("Item", member.Id);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var itemValueType = isKeyed ? SparseChangeSetBasicsEmitter.ElementTypeOf(member) : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
        var isModelValue = isKeyed ? member.Collection.ElementType.IsFragmentModel : member.Collection.ValueType?.IsFragmentModel == true;
        var hasTempKey = isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        code.AppendLine(
            "private static global::System.Collections.Generic.List<"
                + itemType
                + "> ReadItems"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return new();");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload items must be an array.\");"
        );
        code.AppendLine("var list = new global::System.Collections.Generic.List<" + itemType + ">();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;");
        code.AppendLine("list.Add(ReadItem" + member.Id + "(ref reader, options));");
        code.CloseBlock();
        code.AppendLine("return list;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static "
                + itemType
                + " ReadItem"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A payload item must be an object.\");"
        );
        code.AppendLine("var item = new " + itemType + "();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\");"
        );
        code.AppendLine(
            "if (reader.ValueTextEquals(\"key\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.Key = ReadKey<"
                + keyType
                + ">(ref reader, options); }"
        );
        code.AppendLine(
            "else if (reader.ValueTextEquals(\"kind\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.Kind = ParseKind(reader.GetString()); }"
        );
        if (isKeyed)
        {
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"beforeIndex\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.BeforeIndex = reader.GetInt32(); }"
            );
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"afterIndex\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.AfterIndex = reader.GetInt32(); }"
            );
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"isReordered\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.IsReordered = reader.TokenType != global::System.Text.Json.JsonTokenType.Null && reader.GetBoolean(); }"
            );
        }

        code.AppendLine(
            "else if (reader.ValueTextEquals(\"before\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (!TryReadNull(ref reader)) item.Before = ReadEndpoint<"
                + itemValueType
                + ">(ref reader, options, null, null); }"
        );
        code.AppendLine(
            "else if (reader.ValueTextEquals(\"after\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (!TryReadNull(ref reader)) item.After = ReadEndpoint<"
                + itemValueType
                + ">(ref reader, options, null, null); }"
        );
        if (isModelValue)
        {
            if (member.Collection.ValueType is null && !isKeyed)
            {
                throw new global::System.InvalidOperationException("Dictionary item is missing its value model.");
            }

            var editChild = isKeyed ? member.Collection.ElementType.NonNullableName : member.Collection.ValueType.GetValueOrDefault().NonNullableName;
            var editCore = context.ChildDto(editChild, "Core");
            _ = editCore;
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"edit\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) item.Edit = null; else item.Edit = "
                    + editChild
                    + ".PayloadJsonConverter.ReadCore(ref reader, options); }"
            );
        }
        else
        {
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"edit\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); reader.Skip(); }"
            );
        }

        if (hasTempKey)
        {
            code.AppendLine(
                "else if (reader.ValueTextEquals(\"temporaryKey\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); item.TemporaryKey = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : (global::System.Guid?)reader.GetGuid(); }"
            );
        }
        code.AppendLine("else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload item.\"); reader.Skip(); }");
        code.CloseBlock();
        code.AppendLine("return item;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteItems"
                + member.Id
                + "(global::System.Text.Json.Utf8JsonWriter writer, global::System.Collections.Generic.List<"
                + itemType
                + "> items, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine("foreach (var item in items)");
        code.OpenBlock();
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WritePropertyName(\"key\"); WriteKey(writer, item.Key, options);");
        code.AppendLine("writer.WriteString(\"kind\", " + context.Runtime + "ChangePayloadWire.ToToken(item.Kind));");
        if (isKeyed)
        {
            code.AppendLine("writer.WriteNumber(\"beforeIndex\", item.BeforeIndex);");
            code.AppendLine("writer.WriteNumber(\"afterIndex\", item.AfterIndex);");
            code.AppendLine("if (item.IsReordered) writer.WriteBoolean(\"isReordered\", true);");
        }

        code.AppendLine("if (item.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, item.Before, options, null); }");
        code.AppendLine("if (item.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, item.After, options, null); }");
        if (isModelValue)
        {
            if (member.Collection.ValueType is null && !isKeyed)
            {
                throw new global::System.InvalidOperationException("Dictionary item is missing its value model.");
            }

            var writeEditChild = isKeyed
                ? member.Collection.ElementType.NonNullableName
                : member.Collection.ValueType.GetValueOrDefault().NonNullableName;
            code.AppendLine(
                "if (item.Edit is not null) { writer.WritePropertyName(\"edit\"); "
                    + writeEditChild
                    + ".PayloadJsonConverter.WriteCore(writer, item.Edit, options); }"
            );
        }

        if (hasTempKey)
        {
            code.AppendLine(
                "if (item.TemporaryKey is not null) { writer.WritePropertyName(\"temporaryKey\"); writer.WriteStringValue(item.TemporaryKey.Value.ToString()); }"
            );
        }
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine("writer.WriteEndArray();");
        code.CloseBlock();
        code.AppendLine();
    }
}
