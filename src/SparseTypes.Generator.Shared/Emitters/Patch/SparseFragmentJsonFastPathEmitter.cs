using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits trim-safe fast paths for fragment JSON converters.</summary>
internal static class SparseFragmentJsonFastPathEmitter
{
    /// <summary>Emits collection codecs and member fast paths.</summary>
    internal static void AppendFastPaths(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        foreach (var codecMember in members)
        {
            if (codecMember.ChildModel is not null)
            {
                continue;
            }

            SparseJsonCollectionModel? codecModel = null;
            if (
                !SparseJsonCollectionShapes.TryResolve(codecMember, SparseFragmentEmitHelpers.FragmentValueType(codecMember), out codecModel)
                || codecModel is null
            )
            {
                continue;
            }

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
        SparseJsonScalarShapes.AppendScalarHelpers(code, "TMember");
        SparseJsonScalarShapes.AppendMapKeyHelpers(code);
    }

    /// <summary>Emits fast-path fragment member readers and writers.</summary>
    private static void AppendMemberValueHelpers(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "private static TMember? ReadMemberValue<TMember>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { if (default(TMember) is null) return default; throw new global::System.Text.Json.JsonException(\"Malformed fragment value.\"); }"
        );
        code.AppendLine("if (TryReadScalar<TMember>(ref reader, out var scalarMember)) return scalarMember;");
        code.AppendLine("return global::System.Text.Json.JsonSerializer.Deserialize<TMember>(ref reader, GetMemberTypeInfo<TMember>(options))!;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteMemberValue<TMember>(global::System.Text.Json.Utf8JsonWriter writer, TMember? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine(
            "if (!TryWriteScalar(writer, value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value!, GetMemberTypeInfo<TMember>(options));"
        );
        code.CloseBlock();
        code.AppendLine();
    }
}
