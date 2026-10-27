namespace SparseTypes.Generator.Shared;

/// <summary>Emits payload-specific scalar codecs without changing fragment JSON behavior.</summary>
internal static class SparsePayloadJsonScalarShapes
{
    internal static void Append(SharedIndentedBuilder code, string valueParameter, string accessibility, string baseSuffix, string suffix)
    {
        AppendRead(code, valueParameter, accessibility, baseSuffix, suffix);
        code.AppendLine();
        AppendReadEnum(code, valueParameter, accessibility, baseSuffix, suffix);
        code.AppendLine();
        AppendWrite(code, valueParameter, accessibility, baseSuffix, suffix);
        code.AppendLine();
        AppendWriteEnum(code, valueParameter, accessibility, baseSuffix, suffix);
        code.AppendLine();
        AppendParsers(code, suffix);
    }

    private static void AppendRead(SharedIndentedBuilder code, string valueParameter, string accessibility, string baseSuffix, string suffix)
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
        AppendReadType(code, valueParameter, "long", "Read" + suffix + "Int64(ref reader)");
        AppendReadType(code, valueParameter, "ulong", "Read" + suffix + "UInt64(ref reader)");
        AppendReadType(code, valueParameter, "decimal", "Read" + suffix + "Decimal(ref reader)");
        AppendReadType(code, valueParameter, "global::System.Guid", "Read" + suffix + "Guid(ref reader)");
        AppendReadType(code, valueParameter, "global::System.DateTime", "Read" + suffix + "DateTime(ref reader)");
        AppendReadType(code, valueParameter, "global::System.DateTimeOffset", "Read" + suffix + "DateTimeOffset(ref reader)");
        code.AppendLine("if (target.IsEnum)");
        code.OpenBlock();
        code.AppendLine("value = Read" + suffix + "ScalarEnum<" + valueParameter + ">(ref reader);");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine("return TryRead" + baseSuffix + "Scalar(ref reader, out value);");
        code.CloseBlock();
        code.AppendLine("catch (global::System.FormatException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\", exception);");
        code.CloseBlock();
        code.AppendLine("catch (global::System.InvalidOperationException exception)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\", exception);");
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendReadType(SharedIndentedBuilder code, string valueParameter, string type, string expression) =>
        code.AppendLine("if (target == typeof(" + type + ")) { value = (" + valueParameter + "?)(object)" + expression + "; return true; }");

    private static void AppendReadEnum(SharedIndentedBuilder code, string valueParameter, string accessibility, string baseSuffix, string suffix)
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
        code.AppendLine(
            "if (underlying == typeof(long)) return ("
                + valueParameter
                + "?)global::System.Enum.ToObject(enumType, Read"
                + suffix
                + "Int64(ref reader));"
        );
        code.AppendLine(
            "if (underlying == typeof(ulong)) return ("
                + valueParameter
                + "?)global::System.Enum.ToObject(enumType, Read"
                + suffix
                + "UInt64(ref reader));"
        );
        code.AppendLine("return Read" + baseSuffix + "ScalarEnum<" + valueParameter + ">(ref reader);");
        code.CloseBlock();
    }

    private static void AppendWrite(SharedIndentedBuilder code, string valueParameter, string accessibility, string baseSuffix, string suffix)
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
        AppendWriteType(
            code,
            "long",
            "writer.WriteStringValue(((long)(object)value!).ToString(global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        AppendWriteType(
            code,
            "ulong",
            "writer.WriteStringValue(((ulong)(object)value!).ToString(global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        AppendWriteType(
            code,
            "decimal",
            "writer.WriteStringValue(((decimal)(object)value!).ToString(global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        AppendWriteType(code, "global::System.Guid", "writer.WriteStringValue(((global::System.Guid)(object)value!).ToString(\"D\"));");
        code.AppendLine("if (target == typeof(global::System.DateTime))");
        code.OpenBlock();
        code.AppendLine("Write" + suffix + "DateTime(writer, (global::System.DateTime)(object)value!);");
        code.AppendLine("return true;");
        code.CloseBlock();
        AppendWriteType(
            code,
            "global::System.DateTimeOffset",
            "writer.WriteStringValue(((global::System.DateTimeOffset)(object)value!).ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        code.AppendLine("if (target.IsEnum) { Write" + suffix + "ScalarEnum(writer, value!); return true; }");
        code.AppendLine("return TryWrite" + baseSuffix + "Scalar(writer, value);");
        code.CloseBlock();
    }

    private static void AppendWriteType(SharedIndentedBuilder code, string type, string statement) =>
        code.AppendLine("if (target == typeof(" + type + ")) { " + statement + " return true; }");

    private static void AppendWriteEnum(SharedIndentedBuilder code, string valueParameter, string accessibility, string baseSuffix, string suffix)
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
        code.AppendLine(
            "if (underlying == typeof(long)) writer.WriteStringValue(((long)(object)value!).ToString(global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        code.AppendLine(
            "else if (underlying == typeof(ulong)) writer.WriteStringValue(((ulong)(object)value!).ToString(global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        code.AppendLine("else Write" + baseSuffix + "ScalarEnum(writer, value);");
        code.CloseBlock();
    }

    private static void AppendParsers(SharedIndentedBuilder code, string suffix)
    {
        AppendIntegerParser(code, "long", "Int64", "long", "AllowLeadingSign", suffix);
        code.AppendLine();
        AppendIntegerParser(code, "ulong", "UInt64", "ulong", "None", suffix);
        code.AppendLine();
        code.AppendLine("private static decimal Read" + suffix + "Decimal(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString();");
        code.AppendLine(
            "if (text is null || !decimal.TryParse(text, global::System.Globalization.NumberStyles.AllowLeadingSign | global::System.Globalization.NumberStyles.AllowDecimalPoint, global::System.Globalization.CultureInfo.InvariantCulture, out var value) || !global::System.String.Equals(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture), text, global::System.StringComparison.Ordinal)) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");"
        );
        code.AppendLine("return value;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("private static global::System.Guid Read" + suffix + "Guid(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString();");
        code.AppendLine(
            "if (text is null || !global::System.Guid.TryParseExact(text, \"D\", out var value) || !global::System.String.Equals(value.ToString(\"D\"), text, global::System.StringComparison.Ordinal)) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");"
        );
        code.AppendLine("return value;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("private static global::System.DateTime Read" + suffix + "DateTime(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString() ?? throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");");
        code.AppendLine(
            "var value = global::System.DateTime.ParseExact(text, \"O\", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind);"
        );
        code.AppendLine(
            "if (value.Kind == global::System.DateTimeKind.Local || !global::System.String.Equals(value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture), text, global::System.StringComparison.Ordinal)) throw new global::System.Text.Json.JsonException(\"Local DateTime values are not supported by the payload contract.\");"
        );
        code.AppendLine("return value;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static global::System.DateTimeOffset Read" + suffix + "DateTimeOffset(ref global::System.Text.Json.Utf8JsonReader reader)"
        );
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString() ?? throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");");
        code.AppendLine(
            "var value = global::System.DateTimeOffset.ParseExact(text, \"O\", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None);"
        );
        code.AppendLine(
            "if (!global::System.String.Equals(value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture), text, global::System.StringComparison.Ordinal)) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");"
        );
        code.AppendLine("return value;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void Write" + suffix + "DateTime(global::System.Text.Json.Utf8JsonWriter writer, global::System.DateTime value)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (value.Kind == global::System.DateTimeKind.Local) throw new global::System.Text.Json.JsonException(\"Local DateTime values are not supported by the payload contract.\");"
        );
        code.AppendLine("writer.WriteStringValue(value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture));");
        code.CloseBlock();
    }

    private static void AppendIntegerParser(SharedIndentedBuilder code, string type, string name, string parseType, string styles, string suffix)
    {
        code.AppendLine("private static " + type + " Read" + suffix + name + "(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("var text = reader.GetString();");
        code.AppendLine(
            "if (text is null || !"
                + parseType
                + ".TryParse(text, global::System.Globalization.NumberStyles."
                + styles
                + ", global::System.Globalization.CultureInfo.InvariantCulture, out var value) || !global::System.String.Equals(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture), text, global::System.StringComparison.Ordinal)) throw new global::System.Text.Json.JsonException(\"Malformed scalar value.\");"
        );
        code.AppendLine("return value;");
        code.CloseBlock();
    }
}
