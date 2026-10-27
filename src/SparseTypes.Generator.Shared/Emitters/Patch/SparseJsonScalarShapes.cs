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
    /// <param name="valueParameter">Method-level value type parameter name.</param>
    /// <param name="accessibility">Emitted member accessibility.</param>
    internal static void AppendScalarHelpers(SharedIndentedBuilder code, string valueParameter, string accessibility = "private", string suffix = "")
    {
        AppendTryReadScalar(code, valueParameter, accessibility, suffix);
        code.AppendLine();
        AppendReadScalarEnum(code, valueParameter, accessibility, suffix);
        code.AppendLine();
        AppendTryWriteScalar(code, valueParameter, accessibility, suffix);
        code.AppendLine();
        AppendWriteScalarEnum(code, valueParameter, accessibility, suffix);
        code.AppendLine();
    }

    /// <summary>Emits the property-name key helpers for structural map codecs.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="accessibility">Emitted member accessibility.</param>
    internal static void AppendMapKeyHelpers(SharedIndentedBuilder code, string accessibility = "private")
    {
        code.AppendLine(accessibility + " static TKey ParseMapKey<TKey>(string name)");
        code.OpenBlock();
        code.AppendLine("var target = global::System.Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("if (target == typeof(string)) return (TKey)(object)name;");
        code.AppendLine("if (target == typeof(bool)) return (TKey)(object)bool.Parse(name);");
        code.AppendLine("if (target == typeof(char))");
        code.OpenBlock();
        code.AppendLine("if (name.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed collection key.\");");
        code.AppendLine("return (TKey)(object)name[0];");
        code.CloseBlock();
        AppendMapKeyParse(code, "byte", "byte");
        AppendMapKeyParse(code, "sbyte", "sbyte");
        AppendMapKeyParse(code, "short", "short");
        AppendMapKeyParse(code, "ushort", "ushort");
        AppendMapKeyParse(code, "int", "int");
        AppendMapKeyParse(code, "uint", "uint");
        AppendMapKeyParse(code, "long", "long");
        AppendMapKeyParse(code, "ulong", "ulong");
        code.AppendLine("if (target == typeof(global::System.Guid)) return (TKey)(object)new global::System.Guid(name);");
        code.CloseBlock();
        code.AppendLine("catch (global::System.FormatException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Malformed collection key.\", exception);");
        code.CloseBlock();
        code.AppendLine("catch (global::System.InvalidOperationException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Malformed collection key.\", exception);");
        code.CloseBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\");");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(accessibility + " static string FormatMapKey<TKey>(TKey key)");
        code.OpenBlock();
        code.AppendLine("var target = global::System.Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);");
        code.AppendLine("if (target == typeof(string)) return (string)(object)key!;");
        code.AppendLine("if (target == typeof(bool)) return ((bool)(object)key!).ToString();");
        code.AppendLine("if (target == typeof(char)) return ((char)(object)key!).ToString();");
        AppendMapKeyFormat(code, "byte");
        AppendMapKeyFormat(code, "sbyte");
        AppendMapKeyFormat(code, "short");
        AppendMapKeyFormat(code, "ushort");
        AppendMapKeyFormat(code, "int");
        AppendMapKeyFormat(code, "uint");
        AppendMapKeyFormat(code, "long");
        AppendMapKeyFormat(code, "ulong");
        code.AppendLine("if (target == typeof(global::System.Guid)) return ((global::System.Guid)(object)key!).ToString();");
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Unsupported collection key.\");");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendMapKeyParse(SharedIndentedBuilder code, string keyword, string parseType) =>
        code.AppendLine(
            "if (target == typeof("
                + keyword
                + ")) return (TKey)(object)"
                + parseType
                + ".Parse(name, global::System.Globalization.CultureInfo.InvariantCulture);"
        );

    private static void AppendMapKeyFormat(SharedIndentedBuilder code, string keyword) =>
        code.AppendLine(
            "if (target == typeof("
                + keyword
                + ")) return (("
                + keyword
                + ")(object)key!).ToString(global::System.Globalization.CultureInfo.InvariantCulture);"
        );

    private static void AppendTryReadScalar(SharedIndentedBuilder code, string valueParameter, string accessibility, string suffix)
    {
        code.AppendLine(
            accessibility
                + " static bool TryRead"
                + suffix
                + "Scalar<"
                + valueParameter
                + ">(ref global::System.Text.Json.Utf8JsonReader reader, out "
                + valueParameter
                + "? value)"
        );
        code.OpenBlock();
        code.AppendLine("value = default;");
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return false;");
        code.AppendLine("var target = global::System.Nullable.GetUnderlyingType(typeof(" + valueParameter + ")) ?? typeof(" + valueParameter + ");");
        code.AppendLine("try");
        code.OpenBlock();
        AppendScalarRead(code, valueParameter, "string", "reader.GetString()!");
        AppendScalarRead(code, valueParameter, "bool", "reader.GetBoolean()");
        AppendScalarRead(code, valueParameter, "byte", "reader.GetByte()");
        AppendScalarRead(code, valueParameter, "sbyte", "reader.GetSByte()");
        AppendScalarRead(code, valueParameter, "short", "reader.GetInt16()");
        AppendScalarRead(code, valueParameter, "ushort", "reader.GetUInt16()");
        AppendScalarRead(code, valueParameter, "int", "reader.GetInt32()");
        AppendScalarRead(code, valueParameter, "uint", "reader.GetUInt32()");
        AppendScalarRead(code, valueParameter, "long", "reader.GetInt64()");
        AppendScalarRead(code, valueParameter, "ulong", "reader.GetUInt64()");
        AppendScalarRead(code, valueParameter, "float", "reader.GetSingle()");
        AppendScalarRead(code, valueParameter, "double", "reader.GetDouble()");
        AppendScalarRead(code, valueParameter, "decimal", "reader.GetDecimal()");
        code.AppendLine("if (target == typeof(char))");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString();");
        code.AppendLine("if (text is null || text.Length != 1) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");");
        code.AppendLine("value = (" + valueParameter + "?)(object)text[0];");
        code.AppendLine("return true;");
        code.CloseBlock();
        AppendScalarRead(code, valueParameter, "global::System.Guid", "reader.GetGuid()");
        AppendScalarRead(code, valueParameter, "global::System.DateTime", "reader.GetDateTime()");
        AppendScalarRead(code, valueParameter, "global::System.DateTimeOffset", "reader.GetDateTimeOffset()");
        code.AppendLine("if (target.IsEnum)");
        code.OpenBlock();
        code.AppendLine("value = Read" + suffix + "ScalarEnum<" + valueParameter + ">(ref reader);");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.CloseBlock();
        AppendScalarReadCatch(code, "Malformed scalar value.");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendScalarRead(SharedIndentedBuilder code, string valueParameter, string type, string expression) =>
        code.AppendLine("if (target == typeof(" + type + ")) { value = (" + valueParameter + "?)(object)" + expression + "; return true; }");

    private static void AppendScalarReadCatch(SharedIndentedBuilder code, string message)
    {
        code.AppendLine("catch (global::System.FormatException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"" + message + "\", exception);");
        code.CloseBlock();
        code.AppendLine("catch (global::System.InvalidOperationException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"" + message + "\", exception);");
        code.CloseBlock();
    }

    private static void AppendReadScalarEnum(SharedIndentedBuilder code, string valueParameter, string accessibility, string suffix)
    {
        code.AppendLine(
            accessibility
                + " static "
                + valueParameter
                + "? Read"
                + suffix
                + "ScalarEnum<"
                + valueParameter
                + ">(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var enumType = global::System.Nullable.GetUnderlyingType(typeof(" + valueParameter + ")) ?? typeof(" + valueParameter + ");"
        );
        code.AppendLine("var underlying = global::System.Enum.GetUnderlyingType(enumType);");
        code.AppendLine("try");
        code.OpenBlock();
        AppendEnumRead(code, valueParameter, "int", "reader.GetInt32()");
        AppendEnumRead(code, valueParameter, "uint", "reader.GetUInt32()");
        AppendEnumRead(code, valueParameter, "long", "reader.GetInt64()");
        AppendEnumRead(code, valueParameter, "ulong", "reader.GetUInt64()");
        AppendEnumRead(code, valueParameter, "short", "reader.GetInt16()");
        AppendEnumRead(code, valueParameter, "ushort", "reader.GetUInt16()");
        AppendEnumRead(code, valueParameter, "byte", "reader.GetByte()");
        AppendEnumRead(code, valueParameter, "sbyte", "reader.GetSByte()");
        code.CloseBlock();
        AppendScalarReadCatch(code, "Malformed scalar value.");
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Unsupported enum underlying type.\");");
        code.CloseBlock();
    }

    private static void AppendEnumRead(SharedIndentedBuilder code, string valueParameter, string underlying, string expression) =>
        code.AppendLine(
            "if (underlying == typeof("
                + underlying
                + ")) return ("
                + valueParameter
                + "?)global::System.Enum.ToObject(enumType, "
                + expression
                + ");"
        );

    private static void AppendTryWriteScalar(SharedIndentedBuilder code, string valueParameter, string accessibility, string suffix)
    {
        code.AppendLine(
            accessibility
                + " static bool TryWrite"
                + suffix
                + "Scalar<"
                + valueParameter
                + ">(global::System.Text.Json.Utf8JsonWriter writer, "
                + valueParameter
                + "? value)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) return false;");
        code.AppendLine("var target = global::System.Nullable.GetUnderlyingType(typeof(" + valueParameter + ")) ?? typeof(" + valueParameter + ");");
        AppendScalarWrite(code, "string", "writer.WriteStringValue((string)(object)value!);");
        AppendScalarWrite(code, "bool", "writer.WriteBooleanValue((bool)(object)value!);");
        AppendScalarWrite(code, "byte", "writer.WriteNumberValue((int)(byte)(object)value!);");
        AppendScalarWrite(code, "sbyte", "writer.WriteNumberValue((int)(sbyte)(object)value!);");
        AppendScalarWrite(code, "short", "writer.WriteNumberValue((int)(short)(object)value!);");
        AppendScalarWrite(code, "ushort", "writer.WriteNumberValue((int)(ushort)(object)value!);");
        AppendScalarWrite(code, "int", "writer.WriteNumberValue((int)(object)value!);");
        AppendScalarWrite(code, "uint", "writer.WriteNumberValue((uint)(object)value!);");
        AppendScalarWrite(code, "long", "writer.WriteNumberValue((long)(object)value!);");
        AppendScalarWrite(code, "ulong", "writer.WriteNumberValue((ulong)(object)value!);");
        AppendScalarWrite(code, "float", "writer.WriteNumberValue((float)(object)value!);");
        AppendScalarWrite(code, "double", "writer.WriteNumberValue((double)(object)value!);");
        AppendScalarWrite(code, "decimal", "writer.WriteNumberValue((decimal)(object)value!);");
        AppendScalarWrite(code, "char", "writer.WriteStringValue(((char)(object)value!).ToString());");
        AppendScalarWrite(code, "global::System.Guid", "writer.WriteStringValue(((global::System.Guid)(object)value!).ToString());");
        AppendScalarWrite(code, "global::System.DateTime", "writer.WriteStringValue((global::System.DateTime)(object)value!);");
        AppendScalarWrite(code, "global::System.DateTimeOffset", "writer.WriteStringValue((global::System.DateTimeOffset)(object)value!);");
        code.AppendLine("if (target.IsEnum)");
        code.OpenBlock();
        code.AppendLine("Write" + suffix + "ScalarEnum(writer, value!);");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendScalarWrite(SharedIndentedBuilder code, string type, string statement) =>
        code.AppendLine("if (target == typeof(" + type + ")) { " + statement + " return true; }");

    private static void AppendWriteScalarEnum(SharedIndentedBuilder code, string valueParameter, string accessibility, string suffix)
    {
        code.AppendLine(
            accessibility
                + " static void Write"
                + suffix
                + "ScalarEnum<"
                + valueParameter
                + ">(global::System.Text.Json.Utf8JsonWriter writer, "
                + valueParameter
                + " value)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var enumType = global::System.Nullable.GetUnderlyingType(typeof(" + valueParameter + ")) ?? typeof(" + valueParameter + ");"
        );
        code.AppendLine("var underlying = global::System.Enum.GetUnderlyingType(enumType);");
        code.AppendLine("if (underlying == typeof(int)) writer.WriteNumberValue((int)(object)value!);");
        code.AppendLine("else if (underlying == typeof(uint)) writer.WriteNumberValue((uint)(object)value!);");
        code.AppendLine("else if (underlying == typeof(long)) writer.WriteNumberValue((long)(object)value!);");
        code.AppendLine("else if (underlying == typeof(ulong)) writer.WriteNumberValue((ulong)(object)value!);");
        code.AppendLine("else if (underlying == typeof(short)) writer.WriteNumberValue((int)(short)(object)value!);");
        code.AppendLine("else if (underlying == typeof(ushort)) writer.WriteNumberValue((int)(ushort)(object)value!);");
        code.AppendLine("else if (underlying == typeof(byte)) writer.WriteNumberValue((int)(byte)(object)value!);");
        code.AppendLine("else if (underlying == typeof(sbyte)) writer.WriteNumberValue((int)(sbyte)(object)value!);");
        code.AppendLine("else throw new global::System.Text.Json.JsonException(\"Unsupported enum underlying type.\");");
        code.CloseBlock();
    }
}
