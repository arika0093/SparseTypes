namespace SparseTypes.Generator.Shared;

/// <summary>Emits cached enum domain checks for scalar JSON codecs.</summary>
internal static class SparseJsonEnumValidationEmitter
{
    internal static void Append(SharedIndentedBuilder code, string accessibility)
    {
        const string source = """
            /// <summary>Validates numeric enum values against the declared domain.</summary>
            [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
            internal static class ScalarEnumDomain<TEnum>
            {
                internal static object Validate(object value)
                {
                    if (IsFlags ? (Bits(value) & ~DefinedMask) != 0 : !global::System.Enum.IsDefined(EnumType, value))
                        throw new global::System.Text.Json.JsonException("Undefined enum value.");
                    return value;
                }

                private static readonly global::System.Type EnumType = global::System.Nullable.GetUnderlyingType(typeof(TEnum)) ?? typeof(TEnum);
                private static readonly bool IsFlags = EnumType.IsDefined(typeof(global::System.FlagsAttribute), false);
                private static readonly bool IsUnsigned = global::System.Enum.GetUnderlyingType(EnumType) == typeof(ulong);
                private static readonly ulong DefinedMask = CreateMask();

                private static ulong Bits(object value) => IsUnsigned
                    ? global::System.Convert.ToUInt64(value, global::System.Globalization.CultureInfo.InvariantCulture)
                    : unchecked((ulong)global::System.Convert.ToInt64(value, global::System.Globalization.CultureInfo.InvariantCulture));

                private static ulong CreateMask()
                {
                    if (!IsFlags) return 0;
                    ulong mask = 0;
                    foreach (var name in global::System.Enum.GetNames(EnumType)) mask |= Bits(global::System.Enum.Parse(EnumType, name));
                    return mask;
                }
            }
            """;
        foreach (var line in source.Replace("internal static class", accessibility + " static class").Split('\n'))
        {
            code.AppendLine(line.TrimEnd('\r'));
        }
        code.AppendLine();
    }
}
