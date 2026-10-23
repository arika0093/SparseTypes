namespace SparseTypes.Generator.Shared;

/// <summary>Emits preflight checks for payload JSON writes.</summary>
internal static class SparsePayloadJsonPreflightEmitter
{
    /// <summary>Emits recursive checks before the converter writes its first byte.</summary>
    internal static void AppendPreflight(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var coreType = context.Dto("Core");
        var rootChangeType = context.Dto("RootChange");
        var rootType = context.Dto("Root");

        code.AppendLine("internal static void ValidatePayloadForJson(ChangePayload value)");
        code.OpenBlock();
        code.AppendLine("var validation = value.Validate();");
        code.AppendLine(
            "if (!validation.IsValid) throw new global::System.Text.Json.JsonException(\"The change payload does not satisfy its structural contract.\");"
        );
        code.AppendLine("ValidateCoreForJson(value);");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("internal static void ValidateCoreForJson(" + coreType + "? value)");
        code.OpenBlock();
        code.AppendLine("if (value is null) return;");
        code.AppendLine("var validation = value.Validate();");
        code.AppendLine(
            "if (!validation.IsValid) throw new global::System.Text.Json.JsonException(\"The change payload does not satisfy its structural contract.\");"
        );
        code.AppendLine("if (value.Changes is null) return;");
        code.AppendLine("foreach (var change in value.Changes)");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        code.AppendLine("case " + rootChangeType + " root:");
        code.AppendLine("if (root.Before?.State == " + context.Runtime + "ChangePayloadState.Value) ValidateRootForJson(root.Before.Value);");
        code.AppendLine("if (root.After?.State == " + context.Runtime + "ChangePayloadState.Value) ValidateRootForJson(root.After.Value);");
        code.AppendLine("break;");

        foreach (var member in context.Contract.TransportMembers)
        {
            var memberModel = member.Member;
            var variant = context.MemberDto("Change", memberModel.Id);
            code.AppendLine("case " + variant + " member" + memberModel.Id + ":");
            if (!member.IsJsonIncluded)
            {
                code.AppendLine(
                    "throw new global::System.Text.Json.JsonException(\"The change payload contains a member excluded from JSON transport.\");"
                );
            }
            else if (SparseChangeSetBasicsEmitter.IsNested(memberModel))
            {
                var childType = memberModel.ChildModel!.Value.NonNullableName;
                code.AppendLine(
                    "if (member"
                        + memberModel.Id
                        + ".Nested is not null) "
                        + context.ConverterReference(childType)
                        + ".ValidateCoreForJson(member"
                        + memberModel.Id
                        + ".Nested);"
                );
                code.AppendLine(
                    "if (member"
                        + memberModel.Id
                        + ".Value?.State == "
                        + context.Runtime
                        + "ChangePayloadState.Value) "
                        + context.ConverterReference(childType)
                        + ".ValidateRootForJson(member"
                        + memberModel.Id
                        + ".Value.Value);"
                );
                code.AppendLine("break;");
            }
            else if (SparseChangeSetBasicsEmitter.IsKeyed(memberModel) || SparseChangeSetBasicsEmitter.IsDict(memberModel))
            {
                var isModelValue = SparseChangeSetBasicsEmitter.IsKeyed(memberModel)
                    ? memberModel.Collection.ElementType.IsFragmentModel
                    : memberModel.Collection.ValueType?.IsFragmentModel == true;
                if (isModelValue)
                {
                    var childType = SparseChangeSetBasicsEmitter.IsKeyed(memberModel)
                        ? memberModel.Collection.ElementType.NonNullableName
                        : memberModel.Collection.ValueType!.Value.NonNullableName;
                    code.AppendLine("if (member" + memberModel.Id + ".Items is not null)");
                    code.OpenBlock();
                    code.AppendLine("foreach (var item in member" + memberModel.Id + ".Items)");
                    code.OpenBlock();
                    code.AppendLine("if (item?.Edit is not null) " + context.ConverterReference(childType) + ".ValidateCoreForJson(item.Edit);");
                    code.CloseBlock();
                    code.CloseBlock();
                }

                code.AppendLine("break;");
            }
            else
            {
                code.AppendLine("break;");
            }
        }

        code.AppendLine("default: break;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("internal static void ValidateRootForJson(" + rootType + "? value)");
        code.OpenBlock();
        code.AppendLine("if (value is null) return;");
        code.AppendLine(
            "if (value.Members is null) throw new global::System.Text.Json.JsonException(\"A snapshot must contain its member collection.\");"
        );
        code.AppendLine("foreach (var member in value.Members)");
        code.OpenBlock();
        code.AppendLine("switch (member)");
        code.OpenBlock();
        foreach (var member in context.Contract.TransportMembers)
        {
            var memberModel = member.Member;
            var variant = context.MemberDto("Change", memberModel.Id);
            code.AppendLine("case " + variant + " member" + memberModel.Id + ":");
            if (!member.IsJsonIncluded)
            {
                code.AppendLine(
                    "throw new global::System.Text.Json.JsonException(\"The snapshot contains a member excluded from JSON transport.\");"
                );
            }
            else if (SparseChangeSetBasicsEmitter.IsNested(memberModel))
            {
                var childType = memberModel.ChildModel!.Value.NonNullableName;
                code.AppendLine(
                    "if (member"
                        + memberModel.Id
                        + ".Value?.State == "
                        + context.Runtime
                        + "ChangePayloadState.Value) "
                        + context.ConverterReference(childType)
                        + ".ValidateRootForJson(member"
                        + memberModel.Id
                        + ".Value.Value);"
                );
                code.AppendLine("break;");
            }
            else
            {
                code.AppendLine("break;");
            }
        }

        code.AppendLine("default: break;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }
}
