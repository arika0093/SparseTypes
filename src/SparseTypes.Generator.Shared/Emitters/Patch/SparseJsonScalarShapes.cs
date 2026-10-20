namespace SparseTypes.Generator.Shared;

/// <summary>Canonical scalar and enum JSON codecs shared by fragment and payload converters.</summary>
/// <remarks>
/// The helpers emitted here cover the common BCL scalar set (integer widths and
/// signedness, floating point, <c>decimal</c>, <c>bool</c>, <c>char</c>,
/// <c>string</c>, <c>Guid</c>, <c>DateTime</c>, and <c>DateTimeOffset</c>),
/// nullable value-type counterparts, and enums with every CLR-valid underlying
/// integral type. They use canonical 0.1 wire encoding and need no
/// <c>JsonSerializerContext</c> registration: numbers travel as JSON numbers,
/// enums travel as their underlying numeric value, and temporal and identifier
/// values travel as strings. Custom converters, <c>JsonNumberHandling</c>, and
/// naming policies do not alter scalar values; they keep applying to member
/// names and to non-scalar values resolved through options metadata.
/// </remarks>
internal static class SparseJsonScalarShapes
{
    /// <summary>Emits the context-free scalar read and write helpers into one converter.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Member indent of the enclosing converter.</param>
    /// <param name="valueParameter">Method-level value type parameter name.</param>
    internal static void AppendScalarHelpers(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter
    )
    {
        AppendTryReadScalar(code, indent, valueParameter);
        code.AppendLine();
        AppendReadScalarEnum(code, indent, valueParameter);
        code.AppendLine();
        AppendTryWriteScalar(code, indent, valueParameter);
        code.AppendLine();
        AppendWriteScalarEnum(code, indent, valueParameter);
        code.AppendLine();
    }

    /// <summary>Emits the property-name key helpers for structural map codecs.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Member indent of the enclosing converter.</param>
    internal static void AppendMapKeyHelpers(SharedIndentedBuilder code, int indent)
    {
        code.AppendLineAt(indent, "private static TKey ParseMapKey<TKey>(string name)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var target = global::System.Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);"
        );
        code.AppendLineAt(indent + 1, "try");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "if (target == typeof(string)) return (TKey)(object)name;");
        code.AppendLineAt(
            indent + 2,
            "if (target == typeof(bool)) return (TKey)(object)bool.Parse(name);"
        );
        code.AppendLineAt(indent + 2, "if (target == typeof(char))");
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(
            indent + 3,
            "if (name.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed collection key.\");"
        );
        code.AppendLineAt(indent + 3, "return (TKey)(object)name[0];");
        code.AppendLineAt(indent + 2, "}");
        AppendMapKeyParse(code, indent + 2, "byte", "byte");
        AppendMapKeyParse(code, indent + 2, "sbyte", "sbyte");
        AppendMapKeyParse(code, indent + 2, "short", "short");
        AppendMapKeyParse(code, indent + 2, "ushort", "ushort");
        AppendMapKeyParse(code, indent + 2, "int", "int");
        AppendMapKeyParse(code, indent + 2, "uint", "uint");
        AppendMapKeyParse(code, indent + 2, "long", "long");
        AppendMapKeyParse(code, indent + 2, "ulong", "ulong");
        code.AppendLineAt(
            indent + 2,
            "if (target == typeof(global::System.Guid)) return (TKey)(object)new global::System.Guid(name);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "catch (global::System.FormatException exception)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "throw new global::System.Text.Json.JsonException(\"Malformed collection key.\", exception);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "catch (global::System.InvalidOperationException exception)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "throw new global::System.Text.Json.JsonException(\"Malformed collection key.\", exception);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\");"
        );
        code.AppendLineAt(indent, "}");
        code.AppendLine();
        code.AppendLineAt(indent, "private static string FormatMapKey<TKey>(TKey key)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var target = global::System.Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);"
        );
        code.AppendLineAt(indent + 1, "if (target == typeof(string)) return (string)(object)key!;");
        code.AppendLineAt(
            indent + 1,
            "if (target == typeof(bool)) return ((bool)(object)key!).ToString();"
        );
        code.AppendLineAt(
            indent + 1,
            "if (target == typeof(char)) return ((char)(object)key!).ToString();"
        );
        AppendMapKeyFormat(code, indent + 1, "byte");
        AppendMapKeyFormat(code, indent + 1, "sbyte");
        AppendMapKeyFormat(code, indent + 1, "short");
        AppendMapKeyFormat(code, indent + 1, "ushort");
        AppendMapKeyFormat(code, indent + 1, "int");
        AppendMapKeyFormat(code, indent + 1, "uint");
        AppendMapKeyFormat(code, indent + 1, "long");
        AppendMapKeyFormat(code, indent + 1, "ulong");
        code.AppendLineAt(
            indent + 1,
            "if (target == typeof(global::System.Guid)) return ((global::System.Guid)(object)key!).ToString();"
        );
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\");"
        );
        code.AppendLineAt(indent, "}");
        code.AppendLine();
    }

    private static void AppendMapKeyParse(
        SharedIndentedBuilder code,
        int indent,
        string keyword,
        string parseType
    ) =>
        code.AppendLineAt(
            indent,
            "if (target == typeof("
                + keyword
                + ")) return (TKey)(object)"
                + parseType
                + ".Parse(name, global::System.Globalization.CultureInfo.InvariantCulture);"
        );

    private static void AppendMapKeyFormat(
        SharedIndentedBuilder code,
        int indent,
        string keyword
    ) =>
        code.AppendLineAt(
            indent,
            "if (target == typeof("
                + keyword
                + ")) return (("
                + keyword
                + ")(object)key!).ToString(global::System.Globalization.CultureInfo.InvariantCulture);"
        );

    private static void AppendTryReadScalar(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter
    )
    {
        code.AppendLineAt(
            indent,
            "private static bool TryReadScalar<"
                + valueParameter
                + ">(ref global::System.Text.Json.Utf8JsonReader reader, out "
                + valueParameter
                + "? value)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "value = default;");
        code.AppendLineAt(
            indent + 1,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return false;"
        );
        code.AppendLineAt(
            indent + 1,
            "var target = global::System.Nullable.GetUnderlyingType(typeof("
                + valueParameter
                + ")) ?? typeof("
                + valueParameter
                + ");"
        );
        code.AppendLineAt(indent + 1, "try");
        code.AppendLineAt(indent + 1, "{");
        AppendScalarRead(code, indent + 2, valueParameter, "string", "reader.GetString()!");
        AppendScalarRead(code, indent + 2, valueParameter, "bool", "reader.GetBoolean()");
        AppendScalarRead(code, indent + 2, valueParameter, "byte", "reader.GetByte()");
        AppendScalarRead(code, indent + 2, valueParameter, "sbyte", "reader.GetSByte()");
        AppendScalarRead(code, indent + 2, valueParameter, "short", "reader.GetInt16()");
        AppendScalarRead(code, indent + 2, valueParameter, "ushort", "reader.GetUInt16()");
        AppendScalarRead(code, indent + 2, valueParameter, "int", "reader.GetInt32()");
        AppendScalarRead(code, indent + 2, valueParameter, "uint", "reader.GetUInt32()");
        AppendScalarRead(code, indent + 2, valueParameter, "long", "reader.GetInt64()");
        AppendScalarRead(code, indent + 2, valueParameter, "ulong", "reader.GetUInt64()");
        AppendScalarRead(code, indent + 2, valueParameter, "float", "reader.GetSingle()");
        AppendScalarRead(code, indent + 2, valueParameter, "double", "reader.GetDouble()");
        AppendScalarRead(code, indent + 2, valueParameter, "decimal", "reader.GetDecimal()");
        code.AppendLineAt(indent + 2, "if (target == typeof(char))");
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(indent + 3, "var text = reader.GetString();");
        code.AppendLineAt(
            indent + 3,
            "if (text is null || text.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");"
        );
        code.AppendLineAt(indent + 3, "value = (" + valueParameter + "?)(object)text[0];");
        code.AppendLineAt(indent + 3, "return true;");
        code.AppendLineAt(indent + 2, "}");
        AppendScalarRead(
            code,
            indent + 2,
            valueParameter,
            "global::System.Guid",
            "reader.GetGuid()"
        );
        AppendScalarRead(
            code,
            indent + 2,
            valueParameter,
            "global::System.DateTime",
            "reader.GetDateTime()"
        );
        AppendScalarRead(
            code,
            indent + 2,
            valueParameter,
            "global::System.DateTimeOffset",
            "reader.GetDateTimeOffset()"
        );
        code.AppendLineAt(indent + 2, "if (target.IsEnum)");
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(
            indent + 3,
            "value = ReadScalarEnum<" + valueParameter + ">(ref reader);"
        );
        code.AppendLineAt(indent + 3, "return true;");
        code.AppendLineAt(indent + 2, "}");
        code.AppendLineAt(indent + 1, "}");
        AppendScalarReadCatch(code, indent + 1, "Malformed scalar value.");
        code.AppendLineAt(indent + 1, "return false;");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendScalarRead(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter,
        string type,
        string expression
    ) =>
        code.AppendLineAt(
            indent,
            "if (target == typeof("
                + type
                + ")) { value = ("
                + valueParameter
                + "?)(object)"
                + expression
                + "; return true; }"
        );

    private static void AppendScalarReadCatch(
        SharedIndentedBuilder code,
        int indent,
        string message
    )
    {
        code.AppendLineAt(indent, "catch (global::System.FormatException exception)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"" + message + "\", exception);"
        );
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(indent, "catch (global::System.InvalidOperationException exception)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"" + message + "\", exception);"
        );
        code.AppendLineAt(indent, "}");
    }

    private static void AppendReadScalarEnum(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter
    )
    {
        code.AppendLineAt(
            indent,
            "private static "
                + valueParameter
                + "? ReadScalarEnum<"
                + valueParameter
                + ">(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var enumType = global::System.Nullable.GetUnderlyingType(typeof("
                + valueParameter
                + ")) ?? typeof("
                + valueParameter
                + ");"
        );
        code.AppendLineAt(
            indent + 1,
            "var underlying = global::System.Enum.GetUnderlyingType(enumType);"
        );
        code.AppendLineAt(indent + 1, "try");
        code.AppendLineAt(indent + 1, "{");
        AppendEnumRead(code, indent + 2, valueParameter, "int", "reader.GetInt32()");
        AppendEnumRead(code, indent + 2, valueParameter, "uint", "reader.GetUInt32()");
        AppendEnumRead(code, indent + 2, valueParameter, "long", "reader.GetInt64()");
        AppendEnumRead(code, indent + 2, valueParameter, "ulong", "reader.GetUInt64()");
        AppendEnumRead(code, indent + 2, valueParameter, "short", "reader.GetInt16()");
        AppendEnumRead(code, indent + 2, valueParameter, "ushort", "reader.GetUInt16()");
        AppendEnumRead(code, indent + 2, valueParameter, "byte", "reader.GetByte()");
        AppendEnumRead(code, indent + 2, valueParameter, "sbyte", "reader.GetSByte()");
        code.AppendLineAt(indent + 1, "}");
        AppendScalarReadCatch(code, indent + 1, "Malformed scalar value.");
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"Unsupported enum underlying type.\");"
        );
        code.AppendLineAt(indent, "}");
    }

    private static void AppendEnumRead(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter,
        string underlying,
        string expression
    ) =>
        code.AppendLineAt(
            indent,
            "if (underlying == typeof("
                + underlying
                + ")) return ("
                + valueParameter
                + "?)global::System.Enum.ToObject(enumType, "
                + expression
                + ");"
        );

    private static void AppendTryWriteScalar(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter
    )
    {
        code.AppendLineAt(
            indent,
            "private static bool TryWriteScalar<"
                + valueParameter
                + ">(global::System.Text.Json.Utf8JsonWriter writer, "
                + valueParameter
                + "? value)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "if (value is null) return false;");
        code.AppendLineAt(
            indent + 1,
            "var target = global::System.Nullable.GetUnderlyingType(typeof("
                + valueParameter
                + ")) ?? typeof("
                + valueParameter
                + ");"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "string",
            "writer.WriteStringValue((string)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "bool",
            "writer.WriteBooleanValue((bool)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "byte",
            "writer.WriteNumberValue((int)(byte)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "sbyte",
            "writer.WriteNumberValue((int)(sbyte)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "short",
            "writer.WriteNumberValue((int)(short)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "ushort",
            "writer.WriteNumberValue((int)(ushort)(object)value!);"
        );
        AppendScalarWrite(code, indent + 1, "int", "writer.WriteNumberValue((int)(object)value!);");
        AppendScalarWrite(
            code,
            indent + 1,
            "uint",
            "writer.WriteNumberValue((uint)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "long",
            "writer.WriteNumberValue((long)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "ulong",
            "writer.WriteNumberValue((ulong)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "float",
            "writer.WriteNumberValue((float)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "double",
            "writer.WriteNumberValue((double)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "decimal",
            "writer.WriteNumberValue((decimal)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "char",
            "writer.WriteStringValue(((char)(object)value!).ToString());"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "global::System.Guid",
            "writer.WriteStringValue(((global::System.Guid)(object)value!).ToString());"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "global::System.DateTime",
            "writer.WriteStringValue((global::System.DateTime)(object)value!);"
        );
        AppendScalarWrite(
            code,
            indent + 1,
            "global::System.DateTimeOffset",
            "writer.WriteStringValue((global::System.DateTimeOffset)(object)value!);"
        );
        code.AppendLineAt(indent + 1, "if (target.IsEnum)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "WriteScalarEnum(writer, value!);");
        code.AppendLineAt(indent + 2, "return true;");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "return false;");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendScalarWrite(
        SharedIndentedBuilder code,
        int indent,
        string type,
        string statement
    ) =>
        code.AppendLineAt(
            indent,
            "if (target == typeof(" + type + ")) { " + statement + " return true; }"
        );

    private static void AppendWriteScalarEnum(
        SharedIndentedBuilder code,
        int indent,
        string valueParameter
    )
    {
        code.AppendLineAt(
            indent,
            "private static void WriteScalarEnum<"
                + valueParameter
                + ">(global::System.Text.Json.Utf8JsonWriter writer, "
                + valueParameter
                + " value)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var enumType = global::System.Nullable.GetUnderlyingType(typeof("
                + valueParameter
                + ")) ?? typeof("
                + valueParameter
                + ");"
        );
        code.AppendLineAt(
            indent + 1,
            "var underlying = global::System.Enum.GetUnderlyingType(enumType);"
        );
        code.AppendLineAt(
            indent + 1,
            "if (underlying == typeof(int)) writer.WriteNumberValue((int)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(uint)) writer.WriteNumberValue((uint)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(long)) writer.WriteNumberValue((long)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(ulong)) writer.WriteNumberValue((ulong)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(short)) writer.WriteNumberValue((int)(short)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(ushort)) writer.WriteNumberValue((int)(ushort)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(byte)) writer.WriteNumberValue((int)(byte)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (underlying == typeof(sbyte)) writer.WriteNumberValue((int)(sbyte)(object)value!);"
        );
        code.AppendLineAt(
            indent + 1,
            "else throw new global::System.Text.Json.JsonException(\"Unsupported enum underlying type.\");"
        );
        code.AppendLineAt(indent, "}");
    }
}
