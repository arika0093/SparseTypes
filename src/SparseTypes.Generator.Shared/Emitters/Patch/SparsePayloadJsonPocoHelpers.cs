using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits shared dispatch, scalar codecs, and the delegating converter for custom leaf types.</summary>
/// <remarks>Per-leaf readers and writers live in <see cref="SparsePayloadJsonPocoEmitter"/>; this class holds the helpers they all share.</remarks>
internal static class SparsePayloadJsonPocoHelpers
{
    /// <summary>Emits the shared dispatch, scalar codecs, and delegating converter.</summary>
    internal static void AppendHelpers(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        code.AppendLine(
            "private static bool MatchesPocoProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string wireName, string declaredName, bool hasExplicitName)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.ValueTextEquals(wireName)) return true;");
        code.AppendLine("if (!hasExplicitName && options.PropertyNamingPolicy is not null)");
        code.OpenBlock();
        code.AppendLine("var converted = options.PropertyNamingPolicy.ConvertName(declaredName);");
        code.AppendLine("if (reader.ValueTextEquals(converted)) return true;");
        code.AppendLine(
            "if (options.PropertyNameCaseInsensitive && string.Equals(reader.GetString(), converted, global::System.StringComparison.OrdinalIgnoreCase)) return true;"
        );
        code.CloseBlock();
        code.AppendLine(
            "return options.PropertyNameCaseInsensitive && string.Equals(reader.GetString(), wireName, global::System.StringComparison.OrdinalIgnoreCase);"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static bool ShouldOmitPocoValue<TValue>(TValue? value, global::System.Text.Json.JsonSerializerOptions options, int condition)"
        );
        code.OpenBlock();
        // The condition carries raw STJ JsonIgnoreCondition numbering
        // (2 WhenWritingNull, 3 WhenWritingDefault).
        code.AppendLine(
            "if (condition == 2 || options.DefaultIgnoreCondition == global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull) { if (value is null) return true; }"
        );
        code.AppendLine(
            "if (condition == 3 || options.DefaultIgnoreCondition == global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault) { if (global::System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(value!, default!)) return true; }"
        );
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.Text.Json.Serialization.JsonConverter<TValue>? FindPocoConverter<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("foreach (var converter in options.Converters)");
        code.OpenBlock();
        code.AppendLine(
            "if (converter is global::System.Text.Json.Serialization.JsonConverter<TValue> typed && converter.CanConvert(typeof(TValue))) return typed;"
        );
        code.CloseBlock();
        code.AppendLine("return null;");
        code.CloseBlock();
        code.AppendLine();
        AppendScalarReader(code);
        AppendScalarWriter(code);
        code.AppendLine(
            "private static TValue? ReadPocoDirect<TValue>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLine(
                "if (typeof(TValue) == typeof("
                    + poco.RuntimeName
                    + ")) return (TValue)(object)ReadPayloadPoco_"
                    + SparsePayloadJsonPocoEmitter.CodecSuffix(poco, code)
                    + "(ref reader, options);"
            );
        }
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(TValue) + \"'. Combine "
                + context.ResolverReference
                + " with a source-generated context for this value type.\");"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WritePocoDirect<TValue>(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLine(
                "if (typeof(TValue) == typeof("
                    + poco.RuntimeName
                    + ")) { WritePayloadPoco_"
                    + SparsePayloadJsonPocoEmitter.CodecSuffix(poco, code)
                    + "(writer, ("
                    + poco.RuntimeName
                    + "?)(object)value!, options); return; }"
            );
        }
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(TValue) + \"'. Combine "
                + context.ResolverReference
                + " with a source-generated context for this value type.\");"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>? GetPocoInfo<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLine(
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
        code.AppendLine("return null;");
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits nullable-struct branches resolving Nullable value leaves.</summary>
    private static void AppendNullableInfoBranches(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        if (!context.Pocos.Any(static poco => poco.IsValueType))
        {
            return;
        }

        code.AppendLine("var underlying = global::System.Nullable.GetUnderlyingType(typeof(TValue));");
        code.AppendLine("if (underlying is not null)");
        code.OpenBlock();
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            if (!poco.IsValueType)
            {
                continue;
            }

            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLine(
                "if (underlying == typeof("
                    + poco.RuntimeName
                    + ")) return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)(object)global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<"
                    + poco.RuntimeName
                    + "?>(options, PocoNullableConverter<"
                    + poco.RuntimeName
                    + ">.Default);"
            );
        }

        code.CloseBlock();
    }

    /// <summary>Emits the internal delegating converter ahead of the private codecs.</summary>
    internal static void AppendValueConverter(SharedIndentedBuilder code)
    {
        code.AppendLine("/// <summary>Delegating converter routing leaf values through the generated codecs.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("internal sealed class PocoValueConverter<TValue> : global::System.Text.Json.Serialization.JsonConverter<TValue>");
        code.OpenBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override TValue? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var custom = FindPocoConverter<TValue>(options);");
        code.AppendLine("if (custom is not null) return custom.Read(ref reader, typeToConvert, options);");
        code.AppendLine("return ReadPocoDirect<TValue>(ref reader, options);");
        code.CloseBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var custom = FindPocoConverter<TValue>(options);");
        code.AppendLine("if (custom is not null) { custom.Write(writer, value!, options); return; }");
        code.AppendLine("WritePocoDirect(writer, value, options);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Gets the shared converter instance.</summary>");
        code.AppendLine("internal static PocoValueConverter<TValue> Default { get; } = new PocoValueConverter<TValue>();");
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the internal nullable delegating converter for struct leaves.</summary>
    /// <remarks>Nullable annotations vanish for reference types but stay real for value types, so struct leaves need their own branch.</remarks>
    internal static void AppendNullableConverter(SharedIndentedBuilder code)
    {
        code.AppendLine("/// <summary>Delegating converter routing nullable struct leaves through the generated codecs.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "internal sealed class PocoNullableConverter<TValue> : global::System.Text.Json.Serialization.JsonConverter<TValue?> where TValue : struct"
        );
        code.OpenBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override TValue? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine("var custom = FindPocoConverter<TValue?>(options);");
        code.AppendLine("if (custom is not null) return custom.Read(ref reader, typeToConvert, options);");
        code.AppendLine("return ReadPocoDirect<TValue>(ref reader, options);");
        code.CloseBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, TValue? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("var custom = FindPocoConverter<TValue?>(options);");
        code.AppendLine("if (custom is not null) { custom.Write(writer, value, options); return; }");
        code.AppendLine("WritePocoDirect<TValue>(writer, value.Value, options);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Gets the shared converter instance.</summary>");
        code.AppendLine("internal static PocoNullableConverter<TValue> Default { get; } = new PocoNullableConverter<TValue>();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendScalarReader(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "private static T ReadPocoScalar<T>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var custom = FindPocoConverter<T>(options);");
        code.AppendLine("if (custom is not null) return custom.Read(ref reader, typeof(T), options)!;");
        code.AppendLine("if (typeof(T) == typeof(string)) return (T)(object)reader.GetString()!;");
        code.AppendLine("if (typeof(T) == typeof(bool)) return (T)(object)reader.GetBoolean();");
        code.AppendLine("if (typeof(T) == typeof(int)) return (T)(object)reader.GetInt32();");
        code.AppendLine("if (typeof(T) == typeof(long)) return (T)(object)reader.GetInt64();");
        code.AppendLine("if (typeof(T) == typeof(short)) return (T)(object)reader.GetInt16();");
        code.AppendLine("if (typeof(T) == typeof(byte)) return (T)(object)reader.GetByte();");
        code.AppendLine("if (typeof(T) == typeof(sbyte)) return (T)(object)reader.GetSByte();");
        code.AppendLine("if (typeof(T) == typeof(uint)) return (T)(object)reader.GetUInt32();");
        code.AppendLine("if (typeof(T) == typeof(ulong)) return (T)(object)reader.GetUInt64();");
        code.AppendLine("if (typeof(T) == typeof(ushort)) return (T)(object)reader.GetUInt16();");
        code.AppendLine("if (typeof(T) == typeof(float)) return (T)(object)reader.GetSingle();");
        code.AppendLine("if (typeof(T) == typeof(double)) return (T)(object)reader.GetDouble();");
        code.AppendLine("if (typeof(T) == typeof(decimal)) return (T)(object)reader.GetDecimal();");
        code.AppendLine("if (typeof(T) == typeof(global::System.Guid)) return (T)(object)reader.GetGuid();");
        code.AppendLine("if (typeof(T) == typeof(global::System.DateTime)) return (T)(object)reader.GetDateTime();");
        code.AppendLine("if (typeof(T) == typeof(global::System.DateTimeOffset)) return (T)(object)reader.GetDateTimeOffset();");
        code.AppendLine("if (typeof(T) == typeof(byte[])) return (T)(object)reader.GetBytesFromBase64()!;");
        code.AppendLine("if (typeof(T) == typeof(char))");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString();");
        code.AppendLine("if (text is null || text.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\");");
        code.AppendLine("return (T)(object)text[0];");
        code.CloseBlock();
        code.AppendLine("if (typeof(T).IsEnum)");
        code.OpenBlock();
        code.AppendLine(
            "if (global::System.Enum.GetUnderlyingType(typeof(T)) == typeof(ulong)) return (T)global::System.Enum.ToObject(typeof(T), reader.GetUInt64());"
        );
        code.AppendLine("return (T)global::System.Enum.ToObject(typeof(T), reader.GetInt64());");
        code.CloseBlock();
        code.AppendLine("try { return global::System.Text.Json.JsonSerializer.Deserialize<T>(ref reader, GetValueInfo<T>(options))!; }");
        code.AppendLine(
            "catch (global::System.NotSupportedException exception) { throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(T) + \"'. Combine PayloadJson.Default with a source-generated context for custom value types, or add a JsonConverter for the type to the serializer options.\", exception); }"
        );
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendScalarWriter(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "private static void WritePocoScalar<T>(global::System.Text.Json.Utf8JsonWriter writer, T? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("var custom = FindPocoConverter<T>(options);");
        code.AppendLine("if (custom is not null) { custom.Write(writer, value!, options); return; }");
        code.AppendLine("if (typeof(T) == typeof(string)) writer.WriteStringValue((string)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(bool)) writer.WriteBooleanValue((bool)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(int)) writer.WriteNumberValue((int)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(long)) writer.WriteNumberValue((long)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(short)) writer.WriteNumberValue((short)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(byte)) writer.WriteNumberValue((byte)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(sbyte)) writer.WriteNumberValue((sbyte)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(uint)) writer.WriteNumberValue((uint)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(ulong)) writer.WriteNumberValue((ulong)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(ushort)) writer.WriteNumberValue((ushort)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(float)) writer.WriteNumberValue((float)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(double)) writer.WriteNumberValue((double)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(decimal)) writer.WriteNumberValue((decimal)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(global::System.Guid)) writer.WriteStringValue((global::System.Guid)(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(global::System.DateTime)) writer.WriteStringValue((global::System.DateTime)(object)value!);");
        code.AppendLine(
            "else if (typeof(T) == typeof(global::System.DateTimeOffset)) writer.WriteStringValue((global::System.DateTimeOffset)(object)value!);"
        );
        code.AppendLine("else if (typeof(T) == typeof(byte[])) writer.WriteBase64StringValue((byte[])(object)value!);");
        code.AppendLine("else if (typeof(T) == typeof(char)) writer.WriteStringValue(((char)(object)value!).ToString());");
        code.AppendLine("else if (typeof(T).IsEnum)");
        code.OpenBlock();
        code.AppendLine(
            "if (global::System.Enum.GetUnderlyingType(typeof(T)) == typeof(ulong)) writer.WriteNumberValue(global::System.Convert.ToUInt64(value)); else writer.WriteNumberValue(global::System.Convert.ToInt64(value));"
        );
        code.CloseBlock();
        code.AppendLine("else try { global::System.Text.Json.JsonSerializer.Serialize(writer, value!, GetValueInfo<T>(options)); }");
        code.AppendLine(
            "catch (global::System.NotSupportedException exception) { throw new global::System.InvalidOperationException(\"No generated payload JSON codec covers value type '\" + typeof(T) + \"'. Combine PayloadJson.Default with a source-generated context for custom value types, or add a JsonConverter for the type to the serializer options.\", exception); }"
        );
        code.CloseBlock();
        code.AppendLine();
    }
}
