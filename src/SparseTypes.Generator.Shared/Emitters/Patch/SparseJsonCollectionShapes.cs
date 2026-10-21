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
        {
            return false;
        }

        if (SparseChangeSetBasicsEmitter.IsKeyed(member))
        {
            if (!IsSequenceType(valueType))
            {
                return false;
            }

            var element = member.Collection.ElementType;
            if (string.IsNullOrEmpty(element.NonNullableName))
            {
                return false;
            }

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
            {
                return false;
            }

            if (member.Collection.ValueType is not { } value)
            {
                return false;
            }

            if (string.IsNullOrEmpty(value.NonNullableName))
            {
                return false;
            }

            var keyName = NormalizePrimitive(SparseChangeSetBasicsEmitter.KeyTypeOf(member));
            if (!IsSupportedKey(keyName))
            {
                return false;
            }

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
            {
                return false;
            }

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
            {
                return false;
            }

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
            "global::System.Char" => true,
            "global::System.Boolean" => true,
            "global::System.SByte" => true,
            "global::System.Byte" => true,
            "global::System.Int16" => true,
            "global::System.UInt16" => true,
            "global::System.Int32" => true,
            "global::System.UInt32" => true,
            "global::System.Int64" => true,
            "global::System.UInt64" => true,
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
        // Nullable value-type elements need a nullable concrete container so
        // explicit nulls survive the round-trip instead of defaulting.
        var storageElement =
            elementNullable && !element.EndsWith("?", System.StringComparison.Ordinal)
                ? element + "?"
                : element;
        code.AppendLine(
            "private static "
                + declaredType
                + " "
                + readName
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        if (collection.Shape == SparseJsonCollectionShape.Map)
        {
            code.AppendLine(
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
            );
            code.AppendLine(
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A collection must be an object.\");"
            );
            code.AppendLine(
                "var map = new global::System.Collections.Generic.Dictionary<"
                    + keyTypeName
                    + ", "
                    + storageElement
                    + ">();"
            );
            code.AppendLine("while (reader.Read())");
            code.OpenBlock();
            code.AppendLine(
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
            );
            code.AppendLine(
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed collection.\");"
            );
            code.AppendLine(
                "var name = reader.GetString() ?? throw new global::System.Text.Json.JsonException(\"Malformed collection key.\");"
            );
            code.AppendLine(
                "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed collection.\");"
            );
            code.AppendLine(
                "map.Add("
                    + "ParseMapKey<"
                    + keyTypeName
                    + ">(name)"
                    + ", "
                    + ValueReadExpression(element, elementNullable, typeInfoHelper)
                    + ");"
            );
            code.CloseBlock();
            code.AppendLine("return (" + declaredType + ")(object)map;");
        }
        else
        {
            var concrete =
                collection.Shape == SparseJsonCollectionShape.Set
                    ? "global::System.Collections.Generic.HashSet<" + storageElement + ">"
                    : "global::System.Collections.Generic.List<" + storageElement + ">";
            code.AppendLine(
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
            );
            code.AppendLine(
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"A collection must be an array.\");"
            );
            code.AppendLine("var list = new " + concrete + "();");
            code.AppendLine("while (reader.Read())");
            code.OpenBlock();
            code.AppendLine(
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
            );
            code.AppendLine(
                "list.Add(" + ValueReadExpression(element, elementNullable, typeInfoHelper) + ");"
            );
            code.CloseBlock();
            code.AppendLine("return (" + declaredType + ")(object)list;");
        }

        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void "
                + writeName
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + declaredType
                + (declaredType.EndsWith("?", System.StringComparison.Ordinal) ? "" : "?")
                + " value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        if (collection.Shape == SparseJsonCollectionShape.Map)
        {
            code.AppendLine("writer.WriteStartObject();");
            code.AppendLine("foreach (var entry in value)");
            code.OpenBlock();
            code.AppendLine(
                "writer.WritePropertyName(FormatMapKey<" + keyTypeName + ">(entry.Key));"
            );
            code.AppendLine(
                ValueWriteStatement(element, "entry.Value", typeInfoHelper, elementNullable)
            );
            code.CloseBlock();
            code.AppendLine("writer.WriteEndObject();");
        }
        else
        {
            code.AppendLine("writer.WriteStartArray();");
            code.AppendLine("foreach (var element in value)");
            code.OpenBlock();
            code.AppendLine(
                ValueWriteStatement(element, "element", typeInfoHelper, elementNullable)
            );
            code.CloseBlock();
            code.AppendLine("writer.WriteEndArray();");
        }

        code.CloseBlock();
        code.AppendLine();
    }

    private static string ValueReadExpression(string element, bool nullable, string typeInfoHelper)
    {
        // One shared scalar table serves every collection element: covered
        // scalars and enums read context-free, anything else resolves through
        // the converter's options metadata fallback.
        var scalarOrMetadata =
            "(TryReadScalar<"
            + element
            + ">(ref reader, out var scalarElementValue) ? scalarElementValue! : global::System.Text.Json.JsonSerializer.Deserialize<"
            + element
            + ">(ref reader, "
            + typeInfoHelper
            + "<"
            + element
            + ">(options))!)";
        return nullable
            ? "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default! : "
                + scalarOrMetadata
                + ")"
            : "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? throw new global::System.Text.Json.JsonException(\"Malformed collection value.\") : "
                + scalarOrMetadata
                + ")";
    }

    private static string ValueWriteStatement(
        string element,
        string source,
        string typeInfoHelper,
        bool nullable
    )
    {
        var scalarOrMetadata =
            "if (!TryWriteScalar(writer, "
            + source
            + "!)) global::System.Text.Json.JsonSerializer.Serialize(writer, "
            + source
            + "!, "
            + typeInfoHelper
            + "<"
            + element
            + ">(options));";
        return nullable
            ? "if (" + source + " is null) writer.WriteNullValue(); else " + scalarOrMetadata
            : scalarOrMetadata;
    }
}
