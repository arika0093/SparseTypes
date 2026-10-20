using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits shared dispatch, scalar codecs, and the delegating converter for custom leaf types.</summary>
/// <remarks>Per-leaf readers and writers live in <see cref="SparsePayloadJsonPocoEmitter"/>; this class holds the helpers they all share.</remarks>
internal static class SparsePayloadJsonPocoHelpers
{
    /// <summary>Emits the shared dispatch, scalar codecs, and delegating converter.</summary>
    internal static void AppendHelpers(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        code.AppendLineAt(
            2,
            "private static bool MatchesPocoProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string wireName, string declaredName, bool hasExplicitName)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (reader.ValueTextEquals(wireName)) return true;");
        code.AppendLineAt(3, "if (!hasExplicitName && options.PropertyNamingPolicy is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var converted = options.PropertyNamingPolicy.ConvertName(declaredName);"
        );
        code.AppendLineAt(4, "if (reader.ValueTextEquals(converted)) return true;");
        code.AppendLineAt(
            4,
            "if (options.PropertyNameCaseInsensitive && string.Equals(reader.GetString(), converted, global::System.StringComparison.OrdinalIgnoreCase)) return true;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "return options.PropertyNameCaseInsensitive && string.Equals(reader.GetString(), wireName, global::System.StringComparison.OrdinalIgnoreCase);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static bool ShouldOmitPocoValue<TValue>(TValue? value, global::System.Text.Json.JsonSerializerOptions options, int condition)"
        );
        code.AppendLineAt(2, "{");
        // The condition carries raw STJ JsonIgnoreCondition numbering
        // (2 WhenWritingNull, 3 WhenWritingDefault).
        code.AppendLineAt(
            3,
            "if (condition == 2 || options.DefaultIgnoreCondition == global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull) { if (value is null) return true; }"
        );
        code.AppendLineAt(
            3,
            "if (condition == 3 || options.DefaultIgnoreCondition == global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault) { if (global::System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(value!, default!)) return true; }"
        );
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.Serialization.JsonConverter<TValue>? FindPocoConverter<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "foreach (var converter in options.Converters)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (converter is global::System.Text.Json.Serialization.JsonConverter<TValue> typed && converter.CanConvert(typeof(TValue))) return typed;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return null;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        AppendScalarReader(code);
        AppendScalarWriter(code);
        code.AppendLineAt(
            2,
            "private static TValue? ReadPocoDirect<TValue>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(
                3,
                "if (typeof(TValue) == typeof("
                    + poco.RuntimeName
                    + ")) return (TValue)(object)ReadPayloadPoco_"
                    + SparsePayloadJsonPocoEmitter.CodecSuffix(poco, code)
                    + "(ref reader, options);"
            );
        }
        code.AppendLineAt(
            3,
            "throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(TValue) + \"'. Combine "
                + context.ResolverReference
                + " with a source-generated context for this value type.\");"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WritePocoDirect<TValue>(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(
                3,
                "if (typeof(TValue) == typeof("
                    + poco.RuntimeName
                    + ")) { WritePayloadPoco_"
                    + SparsePayloadJsonPocoEmitter.CodecSuffix(poco, code)
                    + "(writer, ("
                    + poco.RuntimeName
                    + "?)(object)value!, options); return; }"
            );
        }
        code.AppendLineAt(
            3,
            "throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(TValue) + \"'. Combine "
                + context.ResolverReference
                + " with a source-generated context for this value type.\");"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>? GetPocoInfo<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(
                3,
                "if (typeof(TValue) == typeof("
                    + poco.RuntimeName
                    + ")) return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)(object)global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<"
                    + poco.RuntimeName
                    + ">(options, PocoValueConverter<"
                    + poco.RuntimeName
                    + ">.Default);"
            );
        }

        AppendNullableInfoBranches(code, context);
        code.AppendLineAt(3, "return null;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits nullable-struct branches resolving Nullable value leaves.</summary>
    private static void AppendNullableInfoBranches(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        if (!context.Pocos.Any(static poco => poco.IsValueType))
            return;
        code.AppendLineAt(
            3,
            "var underlying = global::System.Nullable.GetUnderlyingType(typeof(TValue));"
        );
        code.AppendLineAt(3, "if (underlying is not null)");
        code.AppendLineAt(3, "{");
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            if (!poco.IsValueType)
                continue;
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(
                4,
                "if (underlying == typeof("
                    + poco.RuntimeName
                    + ")) return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)(object)global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<"
                    + poco.RuntimeName
                    + "?>(options, PocoNullableConverter<"
                    + poco.RuntimeName
                    + ">.Default);"
            );
        }

        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits the internal delegating converter ahead of the private codecs.</summary>
    internal static void AppendValueConverter(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Delegating converter routing leaf values through the generated codecs.</summary>"
        );
        code.AppendLineAt(
            2,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            2,
            "internal sealed class PocoValueConverter<TValue> : global::System.Text.Json.Serialization.JsonConverter<TValue>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override TValue? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var custom = FindPocoConverter<TValue>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) return custom.Read(ref reader, typeToConvert, options);"
        );
        code.AppendLineAt(3, "return ReadPocoDirect<TValue>(ref reader, options);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var custom = FindPocoConverter<TValue>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) { custom.Write(writer, value!, options); return; }"
        );
        code.AppendLineAt(3, "WritePocoDirect(writer, value, options);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Gets the shared converter instance.</summary>");
        code.AppendLineAt(
            2,
            "internal static PocoValueConverter<TValue> Default { get; } = new PocoValueConverter<TValue>();"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits the internal nullable delegating converter for struct leaves.</summary>
    /// <remarks>Nullable annotations vanish for reference types but stay real for value types, so struct leaves need their own branch.</remarks>
    internal static void AppendNullableConverter(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Delegating converter routing nullable struct leaves through the generated codecs.</summary>"
        );
        code.AppendLineAt(
            2,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            2,
            "internal sealed class PocoNullableConverter<TValue> : global::System.Text.Json.Serialization.JsonConverter<TValue?> where TValue : struct"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override TValue? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(3, "var custom = FindPocoConverter<TValue?>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) return custom.Read(ref reader, typeToConvert, options);"
        );
        code.AppendLineAt(3, "return ReadPocoDirect<TValue>(ref reader, options);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "var custom = FindPocoConverter<TValue?>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) { custom.Write(writer, value, options); return; }"
        );
        code.AppendLineAt(3, "WritePocoDirect<TValue>(writer, value.Value, options);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Gets the shared converter instance.</summary>");
        code.AppendLineAt(
            2,
            "internal static PocoNullableConverter<TValue> Default { get; } = new PocoNullableConverter<TValue>();"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendScalarReader(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            2,
            "private static T ReadPocoScalar<T>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var custom = FindPocoConverter<T>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) return custom.Read(ref reader, typeof(T), options)!;"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(string)) return (T)(object)reader.GetString()!;"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(bool)) return (T)(object)reader.GetBoolean();"
        );
        code.AppendLineAt(3, "if (typeof(T) == typeof(int)) return (T)(object)reader.GetInt32();");
        code.AppendLineAt(3, "if (typeof(T) == typeof(long)) return (T)(object)reader.GetInt64();");
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(short)) return (T)(object)reader.GetInt16();"
        );
        code.AppendLineAt(3, "if (typeof(T) == typeof(byte)) return (T)(object)reader.GetByte();");
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(sbyte)) return (T)(object)reader.GetSByte();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(uint)) return (T)(object)reader.GetUInt32();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(ulong)) return (T)(object)reader.GetUInt64();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(ushort)) return (T)(object)reader.GetUInt16();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(float)) return (T)(object)reader.GetSingle();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(double)) return (T)(object)reader.GetDouble();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(decimal)) return (T)(object)reader.GetDecimal();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(global::System.Guid)) return (T)(object)reader.GetGuid();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(global::System.DateTime)) return (T)(object)reader.GetDateTime();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(global::System.DateTimeOffset)) return (T)(object)reader.GetDateTimeOffset();"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(byte[])) return (T)(object)reader.GetBytesFromBase64()!;"
        );
        code.AppendLineAt(3, "if (typeof(T) == typeof(char))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var text = reader.GetString();");
        code.AppendLineAt(
            4,
            "if (text is null || text.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\");"
        );
        code.AppendLineAt(4, "return (T)(object)text[0];");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (typeof(T).IsEnum)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (global::System.Enum.GetUnderlyingType(typeof(T)) == typeof(ulong)) return (T)global::System.Enum.ToObject(typeof(T), reader.GetUInt64());"
        );
        code.AppendLineAt(
            4,
            "return (T)global::System.Enum.ToObject(typeof(T), reader.GetInt64());"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "try { return global::System.Text.Json.JsonSerializer.Deserialize<T>(ref reader, GetValueInfo<T>(options))!; }"
        );
        code.AppendLineAt(
            3,
            "catch (global::System.NotSupportedException exception) { throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(T) + \"'. Combine PayloadJson.Default with a source-generated context for custom value types, or add a JsonConverter for the type to the serializer options.\", exception); }"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendScalarWriter(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            2,
            "private static void WritePocoScalar<T>(global::System.Text.Json.Utf8JsonWriter writer, T? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "var custom = FindPocoConverter<T>(options);");
        code.AppendLineAt(
            3,
            "if (custom is not null) { custom.Write(writer, value!, options); return; }"
        );
        code.AppendLineAt(
            3,
            "if (typeof(T) == typeof(string)) writer.WriteStringValue((string)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(bool)) writer.WriteBooleanValue((bool)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(int)) writer.WriteNumberValue((int)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(long)) writer.WriteNumberValue((long)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(short)) writer.WriteNumberValue((short)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(byte)) writer.WriteNumberValue((byte)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(sbyte)) writer.WriteNumberValue((sbyte)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(uint)) writer.WriteNumberValue((uint)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(ulong)) writer.WriteNumberValue((ulong)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(ushort)) writer.WriteNumberValue((ushort)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(float)) writer.WriteNumberValue((float)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(double)) writer.WriteNumberValue((double)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(decimal)) writer.WriteNumberValue((decimal)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(global::System.Guid)) writer.WriteStringValue((global::System.Guid)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(global::System.DateTime)) writer.WriteStringValue((global::System.DateTime)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(global::System.DateTimeOffset)) writer.WriteStringValue((global::System.DateTimeOffset)(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(byte[])) writer.WriteBase64StringValue((byte[])(object)value!);"
        );
        code.AppendLineAt(
            3,
            "else if (typeof(T) == typeof(char)) writer.WriteStringValue(((char)(object)value!).ToString());"
        );
        code.AppendLineAt(3, "else if (typeof(T).IsEnum)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (global::System.Enum.GetUnderlyingType(typeof(T)) == typeof(ulong)) writer.WriteNumberValue(global::System.Convert.ToUInt64(value)); else writer.WriteNumberValue(global::System.Convert.ToInt64(value));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "else try { global::System.Text.Json.JsonSerializer.Serialize(writer, value!, GetValueInfo<T>(options)); }"
        );
        code.AppendLineAt(
            3,
            "catch (global::System.NotSupportedException exception) { throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(T) + \"'. Combine PayloadJson.Default with a source-generated context for custom value types, or add a JsonConverter for the type to the serializer options.\", exception); }"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }
}
