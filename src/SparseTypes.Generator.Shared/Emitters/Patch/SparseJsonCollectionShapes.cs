namespace SparseTypes.Generator.Shared;

/// <summary>Classifies collection value types for trim-safe structural codecs.</summary>
/// <remarks>Structural codecs avoid JsonTypeInfo metadata for the collection itself; only custom element types still need it.</remarks>
internal enum SparseJsonCollectionShape
{
    None,
    Sequence,
    Set,
    Map,
}

/// <summary>Resolved element and key types for one structural collection value.</summary>
internal sealed record SparseJsonCollectionModel(
    SparseJsonCollectionShape Shape,
    string ElementName,
    bool ElementNullable,
    string KeyName
);

/// <summary>Well-known primitive type names with JSON fast paths.</summary>
internal static class SparseJsonPrimitiveNames
{
    /// <summary>Whether the declared value type can carry a JSON null.</summary>
    public static bool IsNullableValue(SparseTypeModel type, string name) =>
        name.EndsWith("?", System.StringComparison.Ordinal) || type.IsReferenceType;
}

/// <summary>Resolves structural collection codecs from analyzed member shapes.</summary>
internal static class SparseJsonCollectionShapes
{
    /// <summary>Tries to resolve a structural codec for one collection endpoint value.</summary>
    /// <param name="member">Owning member.</param>
    /// <param name="valueType">Declared endpoint value type name.</param>
    /// <param name="model">Resolved collection model, or null for the metadata fallback.</param>
    public static bool TryResolve(
        SparseMemberModel member,
        string valueType,
        out SparseJsonCollectionModel? model
    )
    {
        model = null;
        if (SparseChangeSetBasicsEmitter.IsNested(member))
            return false;
        if (SparseChangeSetBasicsEmitter.IsKeyed(member))
        {
            if (!IsSequenceType(valueType))
                return false;
            var element = member.Collection.ElementType;
            if (string.IsNullOrEmpty(element.NonNullableName))
                return false;
            model = new SparseJsonCollectionModel(
                SparseJsonCollectionShape.Sequence,
                NormalizePrimitive(element.NonNullableName),
                SparseJsonPrimitiveNames.IsNullableValue(element, element.Name),
                string.Empty
            );
            return true;
        }

        if (SparseChangeSetBasicsEmitter.IsDict(member))
        {
            if (!IsMapType(valueType))
                return false;
            if (member.Collection.ValueType is not { } value)
                return false;
            if (string.IsNullOrEmpty(value.NonNullableName))
                return false;
            var keyName = NormalizePrimitive(SparseChangeSetBasicsEmitter.KeyTypeOf(member));
            if (!IsSupportedKey(keyName))
                return false;
            model = new SparseJsonCollectionModel(
                SparseJsonCollectionShape.Map,
                NormalizePrimitive(value.NonNullableName),
                SparseJsonPrimitiveNames.IsNullableValue(value, value.Name),
                keyName
            );
            return true;
        }

        if (IsSequenceType(valueType) || IsSetType(valueType))
        {
            var element = member.Collection.ElementType;
            if (string.IsNullOrEmpty(element.NonNullableName))
                return false;
            var shape = IsSetType(valueType)
                ? SparseJsonCollectionShape.Set
                : SparseJsonCollectionShape.Sequence;
            model = new SparseJsonCollectionModel(
                shape,
                NormalizePrimitive(element.NonNullableName),
                SparseJsonPrimitiveNames.IsNullableValue(element, element.Name),
                string.Empty
            );
            return true;
        }

        // Whole-value dictionaries without granular item diffs (for example
        // explicit Replace members) still travel structurally; only the merge
        // path differs, never the wire contract.
        if (
            IsMapType(valueType)
            && member.Collection.CloneKind == SparseCloneCollectionKind.Dictionary
            && member.Collection.ValueType is { } wholeValue
            && !string.IsNullOrEmpty(wholeValue.NonNullableName)
        )
        {
            var wholeKeyName = NormalizePrimitive(member.Collection.ElementType.NonNullableName);
            if (!IsSupportedKey(wholeKeyName))
                return false;
            model = new SparseJsonCollectionModel(
                SparseJsonCollectionShape.Map,
                NormalizePrimitive(wholeValue.NonNullableName),
                SparseJsonPrimitiveNames.IsNullableValue(wholeValue, wholeValue.Name),
                wholeKeyName
            );
            return true;
        }

        return false;
    }

    private static string NormalizePrimitive(string name) =>
        name switch
        {
            "string" => "global::System.String",
            "int" => "global::System.Int32",
            "bool" => "global::System.Boolean",
            "long" => "global::System.Int64",
            "short" => "global::System.Int16",
            "byte" => "global::System.Byte",
            "sbyte" => "global::System.SByte",
            "uint" => "global::System.UInt32",
            "ulong" => "global::System.UInt64",
            "ushort" => "global::System.UInt16",
            "float" => "global::System.Single",
            "double" => "global::System.Double",
            "decimal" => "global::System.Decimal",
            "char" => "global::System.Char",
            "object" => "global::System.Object",
            _ => name,
        };

    private static bool HasPrefix(string valueType, string localName) =>
        valueType.StartsWith(
            "global::System.Collections.Generic." + localName + "<",
            System.StringComparison.Ordinal
        );

    private static bool IsSequenceType(string valueType) =>
        HasPrefix(valueType, "List")
        || HasPrefix(valueType, "IReadOnlyList")
        || HasPrefix(valueType, "IReadOnlyCollection")
        || HasPrefix(valueType, "IEnumerable");

    private static bool IsSetType(string valueType) =>
        HasPrefix(valueType, "HashSet") || HasPrefix(valueType, "ISet");

    private static bool IsMapType(string valueType) =>
        HasPrefix(valueType, "Dictionary")
        || HasPrefix(valueType, "IDictionary")
        || HasPrefix(valueType, "IReadOnlyDictionary");

    private static bool IsSupportedKey(string keyName) =>
        keyName switch
        {
            "global::System.String" => true,
            "global::System.Int32" => true,
            "global::System.Boolean" => true,
            "global::System.Guid" => true,
            _ => false,
        };

    /// <summary>Emits closed structural collection readers and writers.</summary>
    /// <remarks>Closed type arguments keep the generated code trim-safe without reflection.</remarks>
    internal static void AppendCollectionCodec(
        SharedIndentedBuilder code,
        string declaredType,
        SparseJsonCollectionModel collection,
        string readName,
        string writeName,
        string typeInfoHelper
    )
    {
        var element = collection.ElementName;
        var elementNullable = collection.ElementNullable;
        var keyTypeName = collection.KeyName;
        code.AppendLineAt(
            2,
            "private static "
                + declaredType
                + " "
                + readName
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        if (collection.Shape == SparseJsonCollectionShape.Map)
        {
            code.AppendLineAt(
                3,
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
            );
            code.AppendLineAt(
                3,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A collection must be an object.\");"
            );
            code.AppendLineAt(
                3,
                "var map = new global::System.Collections.Generic.Dictionary<"
                    + keyTypeName
                    + ", "
                    + element
                    + ">();"
            );
            code.AppendLineAt(3, "while (reader.Read())");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
            );
            code.AppendLineAt(
                4,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed collection.\");"
            );
            code.AppendLineAt(
                4,
                "var name = reader.GetString() ?? throw new global::System.Text.Json.JsonException(\"Malformed collection key.\");"
            );
            code.AppendLineAt(
                4,
                "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed collection.\");"
            );
            code.AppendLineAt(
                4,
                "map.Add("
                    + KeyFromNameExpression(keyTypeName)
                    + ", "
                    + ValueReadExpression(element, elementNullable, typeInfoHelper)
                    + ");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "return (" + declaredType + ")(object)map;");
        }
        else
        {
            var concrete =
                collection.Shape == SparseJsonCollectionShape.Set
                    ? "global::System.Collections.Generic.HashSet<" + element + ">"
                    : "global::System.Collections.Generic.List<" + element + ">";
            code.AppendLineAt(
                3,
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
            );
            code.AppendLineAt(
                3,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"A collection must be an array.\");"
            );
            code.AppendLineAt(3, "var list = new " + concrete + "();");
            code.AppendLineAt(3, "while (reader.Read())");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
            );
            code.AppendLineAt(
                4,
                "list.Add(" + ValueReadExpression(element, elementNullable, typeInfoHelper) + ");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "return (" + declaredType + ")(object)list;");
        }

        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void "
                + writeName
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + declaredType
                + (declaredType.EndsWith("?", System.StringComparison.Ordinal) ? "" : "?")
                + " value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        if (collection.Shape == SparseJsonCollectionShape.Map)
        {
            code.AppendLineAt(3, "writer.WriteStartObject();");
            code.AppendLineAt(3, "foreach (var entry in value)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "writer.WritePropertyName(" + KeyToNameExpression(keyTypeName, "entry.Key") + ");"
            );
            code.AppendLineAt(
                4,
                ValueWriteStatement(element, "entry.Value", typeInfoHelper, elementNullable)
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "writer.WriteEndObject();");
        }
        else
        {
            code.AppendLineAt(3, "writer.WriteStartArray();");
            code.AppendLineAt(3, "foreach (var element in value)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                ValueWriteStatement(element, "element", typeInfoHelper, elementNullable)
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "writer.WriteEndArray();");
        }

        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static string ValueReadExpression(string element, bool nullable, string typeInfoHelper)
    {
        var direct = DirectReadExpression(element);
        if (direct is not null)
            return nullable
                ? "reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default! : "
                    + direct
                : "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? throw new global::System.Text.Json.JsonException(\"Malformed collection value.\") : "
                    + direct
                    + ")";
        var boxed =
            "(typeof("
            + element
            + ").IsEnum && global::System.Enum.GetUnderlyingType(typeof("
            + element
            + ")) == typeof(int) ? ("
            + element
            + ")global::System.Enum.ToObject(typeof("
            + element
            + "), reader.GetInt32()) : global::System.Text.Json.JsonSerializer.Deserialize<"
            + element
            + ">(ref reader, "
            + typeInfoHelper
            + "<"
            + element
            + ">(options))!)";
        return nullable
            ? "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default! : "
                + boxed
                + ")"
            : "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? throw new global::System.Text.Json.JsonException(\"Malformed collection value.\") : "
                + boxed
                + ")";
    }

    private static string ValueWriteStatement(
        string element,
        string source,
        string typeInfoHelper,
        bool nullable
    )
    {
        var direct = DirectWriteExpression(element, source);
        if (direct is not null)
            return nullable
                ? "if (" + source + " is null) writer.WriteNullValue(); else " + direct
                : direct;
        var boxed =
            "if (typeof("
            + element
            + ").IsEnum && global::System.Enum.GetUnderlyingType(typeof("
            + element
            + ")) == typeof(int)) writer.WriteNumberValue((int)(object)"
            + source
            + "!); else global::System.Text.Json.JsonSerializer.Serialize(writer, "
            + source
            + "!, "
            + typeInfoHelper
            + "<"
            + element
            + ">(options));";
        return nullable
            ? "if (" + source + " is null) writer.WriteNullValue(); else " + boxed
            : boxed;
    }

    private static string? DirectReadExpression(string element) =>
        element switch
        {
            "global::System.String" => "reader.GetString()!",
            "global::System.Int32" => "reader.GetInt32()",
            "global::System.Boolean" => "reader.GetBoolean()",
            "global::System.Guid" => "reader.GetGuid()",
            _ => null,
        };

    private static string? DirectWriteExpression(string element, string source) =>
        element switch
        {
            "global::System.String" => "writer.WriteStringValue((string)(object)" + source + "!);",
            "global::System.Int32" => "writer.WriteNumberValue((int)(object)" + source + "!);",
            "global::System.Boolean" => "writer.WriteBooleanValue((bool)(object)" + source + "!);",
            "global::System.Guid" => "writer.WriteStringValue(((global::System.Guid)(object)"
                + source
                + "!)"
                + ".ToString());",
            _ => null,
        };

    private static string KeyFromNameExpression(string keyType) =>
        keyType switch
        {
            "global::System.String" => "name",
            "global::System.Int32" =>
                "int.Parse(name, global::System.Globalization.CultureInfo.InvariantCulture)",
            "global::System.Boolean" => "bool.Parse(name)",
            "global::System.Guid" => "new global::System.Guid(name)",
            _ =>
                "throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\")",
        };

    private static string KeyToNameExpression(string keyType, string source) =>
        keyType switch
        {
            "global::System.String" => source,
            "global::System.Int32" => source
                + ".ToString(global::System.Globalization.CultureInfo.InvariantCulture)",
            "global::System.Boolean" => source + ".ToString()",
            "global::System.Guid" => source + ".ToString()",
            _ =>
                "throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\")",
        };
}
