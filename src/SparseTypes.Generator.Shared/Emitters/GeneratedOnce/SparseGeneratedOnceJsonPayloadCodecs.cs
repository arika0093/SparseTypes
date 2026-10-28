using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds the compilation-scoped shared payload JSON codec source.</summary>
/// <remarks>Collection key lists and custom leaf match helpers need resolver-era serializer APIs, so they ship separately from the wire-level core.</remarks>
internal static class SparseGeneratedOnceJsonPayloadCodecs
{
    /// <summary>Builds shared payload JSON codecs in the configured namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="emitCodecs">Whether the consumer has the required serializer API.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(string implementationNamespace, bool emitCodecs, CancellationToken cancellationToken)
    {
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(code, implementationNamespace, cancellationToken);
        if (!emitCodecs)
        {
            code.CloseBlock();
            return code.ToString();
        }

        code.AppendLine("/// <summary>Shared payload JSON codecs for generated change payload converters.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("internal static class " + SparseGeneratedOnceNames.JsonPayloadCodecs);
        code.OpenBlock();
        AppendKeyLists(code);
        code.AppendLine();
        AppendGuidLists(code);
        code.AppendLine();
        AppendPocoMatchHelpers(code);
        code.CloseBlock();
        code.CloseBlock();
        return code.ToString();
    }

    private static void AppendKeyLists(SharedIndentedBuilder code)
    {
        var codecs = SparseGeneratedOnceNames.JsonCodecs;
        code.AppendLine(
            "internal static global::System.Collections.Generic.List<TKey>? ReadKeyList<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TKey>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("var list = new global::System.Collections.Generic.List<TKey>();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;");
        code.AppendLine("list.Add(" + codecs + ".ReadKey(ref reader, options, getTypeInfo));");
        code.CloseBlock();
        code.AppendLine("return list;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteKeyList<TKey>(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TKey>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine("foreach (TKey item in values) " + codecs + ".WriteKey(writer, item, options, getTypeInfo);");
        code.AppendLine("writer.WriteEndArray();");
        code.CloseBlock();
    }

    private static void AppendGuidLists(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "internal static global::System.Collections.Generic.List<global::System.Guid>? ReadGuidList(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("var list = new global::System.Collections.Generic.List<global::System.Guid>();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String || !reader.TryGetGuid(out var value)) throw new global::System.Text.Json.JsonException(\"Malformed temporary key.\"); list.Add(value);"
        );
        code.CloseBlock();
        code.AppendLine("return list;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("internal static void WriteGuidList(global::System.Text.Json.Utf8JsonWriter writer, System.Collections.IEnumerable values)");
        code.OpenBlock();
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine("foreach (global::System.Guid item in values) writer.WriteStringValue(item.ToString());");
        code.AppendLine("writer.WriteEndArray();");
        code.CloseBlock();
    }

    private static void AppendPocoMatchHelpers(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "internal static bool MatchesPocoProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string wireName, string declaredName, bool hasExplicitName)"
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
            "internal static bool ShouldOmitPocoValue<TValue>(TValue? value, global::System.Text.Json.JsonSerializerOptions options, int condition)"
        );
        code.OpenBlock();
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
            "internal static global::System.Text.Json.Serialization.JsonConverter<TValue>? FindPocoConverter<TValue>(global::System.Text.Json.JsonSerializerOptions options)"
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
    }
}
