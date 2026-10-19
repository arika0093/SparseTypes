using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits trim-safe fast paths for fragment JSON converters.</summary>
internal static class SparseFragmentJsonFastPathEmitter
{
    /// <summary>Emits collection codecs and member fast paths.</summary>
    internal static void AppendFastPaths(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        foreach (var codecMember in members)
        {
            if (codecMember.ChildModel is not null)
                continue;
            SparseJsonCollectionModel? codecModel = null;
            if (
                !SparseJsonCollectionShapes.TryResolve(
                    codecMember,
                    SparseFragmentEmitHelpers.FragmentValueType(codecMember),
                    out codecModel
                ) || codecModel is null
            )
                continue;
            SparseJsonCollectionShapes.AppendCollectionCodec(
                code,
                SparseFragmentEmitHelpers.FragmentValueType(codecMember),
                codecModel,
                "ReadCollection" + codecMember.Id,
                "WriteCollection" + codecMember.Id,
                "GetMemberTypeInfo"
            );
        }
        AppendMemberValueHelpers(code);
    }

    /// <summary>Emits fast-path fragment member readers and writers.</summary>
    private static void AppendMemberValueHelpers(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            3,
            "private static TMember? ReadMemberValue<TMember>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { if (default(TMember) is null) return default; throw new global::System.Text.Json.JsonException(\"Malformed fragment value.\"); }"
        );
        code.AppendLineAt(
            4,
            "if (typeof(TMember) == typeof(string)) return (TMember)(object)reader.GetString()!;"
        );
        code.AppendLineAt(
            4,
            "if (typeof(TMember) == typeof(int)) return (TMember)(object)reader.GetInt32();"
        );
        code.AppendLineAt(
            4,
            "if (typeof(TMember) == typeof(bool)) return (TMember)(object)reader.GetBoolean();"
        );
        code.AppendLineAt(
            4,
            "if (typeof(TMember) == typeof(global::System.Guid)) return (TMember)(object)reader.GetGuid();"
        );
        code.AppendLineAt(
            4,
            "if (typeof(TMember).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TMember)) == typeof(int)) return (TMember)global::System.Enum.ToObject(typeof(TMember), reader.GetInt32());"
        );
        code.AppendLineAt(
            4,
            "return global::System.Text.Json.JsonSerializer.Deserialize<TMember>(ref reader, GetMemberTypeInfo<TMember>(options))!;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static void WriteMemberValue<TMember>(global::System.Text.Json.Utf8JsonWriter writer, TMember? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(
            4,
            "if (typeof(TMember) == typeof(string)) writer.WriteStringValue((string)(object)value!);"
        );
        code.AppendLineAt(
            4,
            "else if (typeof(TMember) == typeof(int)) writer.WriteNumberValue((int)(object)value!);"
        );
        code.AppendLineAt(
            4,
            "else if (typeof(TMember) == typeof(bool)) writer.WriteBooleanValue((bool)(object)value!);"
        );
        code.AppendLineAt(
            4,
            "else if (typeof(TMember) == typeof(global::System.Guid)) writer.WriteStringValue(((global::System.Guid)(object)value!).ToString());"
        );
        code.AppendLineAt(
            4,
            "else if (typeof(TMember).IsEnum && global::System.Enum.GetUnderlyingType(typeof(TMember)) == typeof(int)) writer.WriteNumberValue((int)(object)value!);"
        );
        code.AppendLineAt(
            4,
            "else global::System.Text.Json.JsonSerializer.Serialize(writer, value!, GetMemberTypeInfo<TMember>(options));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
    }
}
