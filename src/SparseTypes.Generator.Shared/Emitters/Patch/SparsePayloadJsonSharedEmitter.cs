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
        code.AppendLineAt(
            2,
            "private static bool TryReadNull(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return true;"
        );
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "ChangePayloadState ParseState(string? token) => token switch"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "\"missing\" => " + runtime + "ChangePayloadState.Missing,");
        code.AppendLineAt(3, "\"null\" => " + runtime + "ChangePayloadState.Null,");
        code.AppendLineAt(3, "\"value\" => " + runtime + "ChangePayloadState.Value,");
        code.AppendLineAt(3, "\"redacted\" => " + runtime + "ChangePayloadState.Redacted,");
        code.AppendLineAt(
            3,
            "_ => throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\"),"
        );
        code.AppendLineAt(2, "};");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "ChangePayloadItemKind ParseKind(string? token) => token switch"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "\"add\" => " + runtime + "ChangePayloadItemKind.Add,");
        code.AppendLineAt(3, "\"remove\" => " + runtime + "ChangePayloadItemKind.Remove,");
        code.AppendLineAt(3, "\"edit\" => " + runtime + "ChangePayloadItemKind.Edit,");
        code.AppendLineAt(3, "\"reorder\" => " + runtime + "ChangePayloadItemKind.Reorder,");
        code.AppendLineAt(
            3,
            "_ => throw new global::System.Text.Json.JsonException(\"Unknown item kind.\"),"
        );
        code.AppendLineAt(2, "};");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private delegate T RootReader<T>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options);"
        );
        code.AppendLineAt(
            2,
            "private delegate void RootWriter<T>(global::System.Text.Json.Utf8JsonWriter writer, T? value, global::System.Text.Json.JsonSerializerOptions options);"
        );
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "ChangePayloadEndpoint<TValue>? ReadEndpoint<TValue>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, RootReader<TValue>? rootReader, RootWriter<TValue>? rootWriter)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLineAt(3, "var state = default(" + runtime + "ChangePayloadState);");
        code.AppendLineAt(3, "var hasState = false;");
        code.AppendLineAt(3, "TValue? value = default;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLineAt(
            4,
            "if (reader.ValueTextEquals(\"state\"u8)) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); state = ParseState(reader.GetString()); hasState = true; }"
        );
        code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"value\"u8))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) value = default;"
        );
        code.AppendLineAt(
            5,
            "else if (rootReader is not null) value = rootReader(ref reader, options);"
        );
        code.AppendLineAt(
            5,
            "else if (typeof(TValue) == typeof(string)) value = (TValue)(object)reader.GetString()!;"
        );
        code.AppendLineAt(
            5,
            "else if (typeof(TValue) == typeof(int)) value = (TValue)(object)reader.GetInt32();"
        );
        code.AppendLineAt(
            5,
            "else if (typeof(TValue) == typeof(bool)) value = (TValue)(object)reader.GetBoolean();"
        );
        code.AppendLineAt(
            5,
            "else if (typeof(TValue) == typeof(global::System.Guid)) value = (TValue)(object)reader.GetGuid();"
        );
        code.AppendLineAt(
            5,
            "else if (typeof(TValue).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TValue)) == typeof(int)) value = (TValue)global::System.Enum.ToObject(typeof(TValue), reader.GetInt32());"
        );
        code.AppendLineAt(
            5,
            "else value = global::System.Text.Json.JsonSerializer.Deserialize<TValue>(ref reader, GetValueInfo<TValue>(options));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\"); reader.Skip(); }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");"
        );
        code.AppendLineAt(
            3,
            "return new "
                + runtime
                + "ChangePayloadEndpoint<TValue> { State = state, Value = value };"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteEndpoint<TValue>(global::System.Text.Json.Utf8JsonWriter writer, "
                + runtime
                + "ChangePayloadEndpoint<TValue>? value, global::System.Text.Json.JsonSerializerOptions options, RootWriter<TValue>? rootWriter)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(
            3,
            "writer.WriteString(\"state\", " + runtime + "ChangePayloadWire.ToToken(value.State));"
        );
        code.AppendLineAt(3, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(3, "if (value.Value is null) writer.WriteNullValue();");
        code.AppendLineAt(
            3,
            "else if (rootWriter is not null) rootWriter(writer, value.Value!, options);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TValue) == typeof(string)) writer.WriteStringValue((string)(object)value.Value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TValue) == typeof(int)) writer.WriteNumberValue((int)(object)value.Value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TValue) == typeof(bool)) writer.WriteBooleanValue((bool)(object)value.Value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TValue) == typeof(global::System.Guid)) writer.WriteStringValue(((global::System.Guid)(object)value.Value!).ToString());"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TValue).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TValue)) == typeof(int)) writer.WriteNumberValue((int)(object)value.Value!);"
        );
        code.AppendLineAt(
            3,
            "else global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value!, GetValueInfo<TValue>(options));"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> GetValueInfo<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "try { return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue)); }"
        );
        code.AppendLineAt(3, "catch (global::System.NotSupportedException exception)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "throw new global::System.InvalidOperationException(\"The generated payload resolver requires JsonTypeInfo metadata for value type '\" + typeof(TValue) + \"'. Combine PayloadJson.Default with a source-generated context for custom value types.\", exception);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static TKey ReadKey<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (typeof(TKey) == typeof(string)) return (TKey)(object)reader.GetString()!;"
        );
        code.AppendLineAt(
            3,
            "if (typeof(TKey) == typeof(int)) return (TKey)(object)reader.GetInt32();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(TKey) == typeof(global::System.Guid)) return (TKey)(object)reader.GetGuid();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(TKey).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TKey)) == typeof(int)) return (TKey)global::System.Enum.ToObject(typeof(TKey), reader.GetInt32());"
        );
        code.AppendLineAt(
            3,
            "return global::System.Text.Json.JsonSerializer.Deserialize<TKey>(ref reader, GetValueInfo<TKey>(options))!;"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteKey<TKey>(global::System.Text.Json.Utf8JsonWriter writer, TKey key, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (typeof(TKey) == typeof(string)) writer.WriteStringValue((string)(object)key!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TKey) == typeof(int)) writer.WriteNumberValue((int)(object)key!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TKey) == typeof(global::System.Guid)) writer.WriteStringValue(((global::System.Guid)(object)key!).ToString());"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(TKey).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TKey)) == typeof(int)) writer.WriteNumberValue((int)(object)key!);"
        );
        code.AppendLineAt(
            3,
            "else global::System.Text.Json.JsonSerializer.Serialize(writer, key!, GetValueInfo<TKey>(options));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static global::System.Collections.Generic.List<TKey>? ReadKeyList<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLineAt(3, "var list = new global::System.Collections.Generic.List<TKey>();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(4, "list.Add(ReadKey<TKey>(ref reader, options));");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return list;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteKeyList<TKey>(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartArray();");
        code.AppendLineAt(3, "foreach (TKey item in values) WriteKey(writer, item, options);");
        code.AppendLineAt(3, "writer.WriteEndArray();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static global::System.Collections.Generic.List<global::System.Guid>? ReadGuidList(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLineAt(
            3,
            "var list = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(4, "list.Add(reader.GetGuid());");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return list;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteGuidList(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartArray();");
        code.AppendLineAt(
            3,
            "foreach (global::System.Guid item in values) writer.WriteStringValue(item.ToString());"
        );
        code.AppendLineAt(3, "writer.WriteEndArray();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }
}
