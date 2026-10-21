namespace SparseTypes.Generator.Shared;

/// <summary>Shared endpoint, key, and envelope helpers for payload JSON.</summary>
internal static class SparsePayloadJsonSharedEmitter
{
    /// <summary>Emits shared helpers inside the converter.</summary>
    internal static void AppendShared(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        var runtime = context.Runtime;
        code.AppendLine(
            "private static bool TryReadNull(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return true;"
        );
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static "
                + runtime
                + "ChangePayloadState ParseState(string? token) => token switch"
        );
        code.OpenBlock();
        code.AppendLine("\"missing\" => " + runtime + "ChangePayloadState.Missing,");
        code.AppendLine("\"null\" => " + runtime + "ChangePayloadState.Null,");
        code.AppendLine("\"value\" => " + runtime + "ChangePayloadState.Value,");
        code.AppendLine("\"redacted\" => " + runtime + "ChangePayloadState.Redacted,");
        code.AppendLine(
            "_ => throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\"),"
        );
        code.Unindent().AppendLine("};");
        code.AppendLine();
        code.AppendLine(
            "private static "
                + runtime
                + "ChangePayloadItemKind ParseKind(string? token) => token switch"
        );
        code.OpenBlock();
        code.AppendLine("\"add\" => " + runtime + "ChangePayloadItemKind.Add,");
        code.AppendLine("\"remove\" => " + runtime + "ChangePayloadItemKind.Remove,");
        code.AppendLine("\"edit\" => " + runtime + "ChangePayloadItemKind.Edit,");
        code.AppendLine("\"reorder\" => " + runtime + "ChangePayloadItemKind.Reorder,");
        code.AppendLine(
            "_ => throw new global::System.Text.Json.JsonException(\"Unknown item kind.\"),"
        );
        code.Unindent().AppendLine("};");
        code.AppendLine();
        code.AppendLine(
            "private delegate T RootReader<T>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options);"
        );
        code.AppendLine(
            "private delegate void RootWriter<T>(global::System.Text.Json.Utf8JsonWriter writer, T? value, global::System.Text.Json.JsonSerializerOptions options);"
        );
        code.AppendLine();
        code.AppendLine(
            "private static "
                + runtime
                + "ChangePayloadEndpoint<TValue>? ReadEndpoint<TValue>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, RootReader<TValue>? rootReader, RootWriter<TValue>? rootWriter)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLine("var state = default(" + runtime + "ChangePayloadState);");
        code.AppendLine("var hasState = false;");
        code.AppendLine("TValue? value = default;");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLine(
            "if (reader.ValueTextEquals(\"state\")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); state = ParseState(reader.GetString()); hasState = true; }"
        );
        code.AppendLine("else if (reader.ValueTextEquals(\"value\"))");
        code.OpenBlock();
        code.AppendLine(
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) value = default;"
        );
        code.AppendLine(
            "else if (rootReader is not null) value = rootReader(ref reader, options);"
        );
        code.AppendLine(
            "else if (TryReadScalar<TValue>(ref reader, out var scalarValue)) value = scalarValue;"
        );
        code.AppendLine(
            "else value = global::System.Text.Json.JsonSerializer.Deserialize<TValue>(ref reader, GetValueInfo<TValue>(options));"
        );
        code.CloseBlock();
        code.AppendLine(
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); reader.Skip(); }"
        );
        code.CloseBlock();
        code.AppendLine(
            "if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");"
        );
        code.AppendLine(
            "return new "
                + runtime
                + "ChangePayloadEndpoint<TValue> { State = state, Value = value };"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteEndpoint<TValue>(global::System.Text.Json.Utf8JsonWriter writer, "
                + runtime
                + "ChangePayloadEndpoint<TValue>? value, global::System.Text.Json.JsonSerializerOptions options, RootWriter<TValue>? rootWriter)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine(
            "writer.WriteString(\"state\", " + runtime + "ChangePayloadWire.ToToken(value.State));"
        );
        code.AppendLine("writer.WritePropertyName(\"value\");");
        code.AppendLine("if (value.Value is null) writer.WriteNullValue();");
        code.AppendLine(
            "else if (rootWriter is not null) rootWriter(writer, value.Value!, options);"
        );
        code.AppendLine(
            "else if (!TryWriteScalar(writer, value.Value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value!, GetValueInfo<TValue>(options));"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> GetValueInfo<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        if (SparsePayloadJsonPocoEmitter.HasPocos(context))
        {
            code.AppendLine("var poco = GetPocoInfo<TValue>(options);");
            code.AppendLine("if (poco is not null) return poco;");
        }

        code.AppendLine(
            "try { return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue)); }"
        );
        code.AppendLine("catch (global::System.NotSupportedException exception)");
        code.OpenBlock();
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"The generated payload resolver requires JsonTypeInfo metadata for value type '\" + typeof(TValue) + \"'. Combine "
                + context.ResolverReference
                + " with a source-generated context for custom value types, or add a JsonConverter for the type to the serializer options.\", exception);"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static TKey ReadKey<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) throw new global::System.Text.Json.JsonException(\"Malformed key.\");"
        );
        code.AppendLine(
            "if (TryReadScalar<TKey>(ref reader, out var scalarKey)) return scalarKey!;"
        );
        code.AppendLine(
            "return global::System.Text.Json.JsonSerializer.Deserialize<TKey>(ref reader, GetValueInfo<TKey>(options))!;"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteKey<TKey>(global::System.Text.Json.Utf8JsonWriter writer, TKey key, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (!TryWriteScalar(writer, key!)) global::System.Text.Json.JsonSerializer.Serialize(writer, key!, GetValueInfo<TKey>(options));"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.Collections.Generic.List<TKey>? ReadKeyList<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("var list = new global::System.Collections.Generic.List<TKey>();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLine("list.Add(ReadKey<TKey>(ref reader, options));");
        code.CloseBlock();
        code.AppendLine("return list;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteKeyList<TKey>(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine("foreach (TKey item in values) WriteKey(writer, item, options);");
        code.AppendLine("writer.WriteEndArray();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.Collections.Generic.List<global::System.Guid>? ReadGuidList(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine(
            "var list = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLine("list.Add(reader.GetGuid());");
        code.CloseBlock();
        code.AppendLine("return list;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteGuidList(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values)"
        );
        code.OpenBlock();
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine(
            "foreach (global::System.Guid item in values) writer.WriteStringValue(item.ToString());"
        );
        code.AppendLine("writer.WriteEndArray();");
        code.CloseBlock();
        code.AppendLine();
        SparseJsonScalarShapes.AppendScalarHelpers(code, "TValue");
        SparseJsonScalarShapes.AppendMapKeyHelpers(code);
    }
}
