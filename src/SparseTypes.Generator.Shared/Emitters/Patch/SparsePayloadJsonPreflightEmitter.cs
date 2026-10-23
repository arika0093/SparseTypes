using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits preflight checks for payload JSON writes.</summary>
internal static class SparsePayloadJsonPreflightEmitter
{
    /// <summary>Emits recursive checks before the converter writes its first byte.</summary>
    internal static void AppendPreflight(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var coreType = context.Dto("Core");
        var rootType = context.Dto("Root");

        code.AppendLine("internal static void ValidatePayloadForJson(" + context.ModelType + ".ChangePayload value)");
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
        code.AppendLine("if (value.TypedChange is null) return;");
        var modelChangeKind = context.Dto("ModelChangeKind");
        code.AppendLine("if (value.TypedChange.Kind == " + modelChangeKind + ".Replace)");
        code.OpenBlock();
        code.AppendLine("var before = value.TypedChange.Before;");
        code.AppendLine("var after = value.TypedChange.After;");
        code.AppendLine("if (before?.State == " + context.Runtime + "ChangePayloadState.Value) ValidateRootForJson(before.Value);");
        code.AppendLine("if (after?.State == " + context.Runtime + "ChangePayloadState.Value) ValidateRootForJson(after.Value);");
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine("foreach (var change in value.EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
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
        foreach (
            var propertyName in context
                .Contract.TransportMembers.Where(static contractMember => !contractMember.IsJsonIncluded)
                .Select(static contractMember => contractMember.Member.Property.Name)
        )
        {
            var escapedPropertyName = SparseNaming.EscapeIdentifier(propertyName);
            var message = global::Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                "The snapshot contains a member excluded from JSON transport: " + propertyName + ".",
                true
            );
            code.AppendLine("if (value." + escapedPropertyName + " is not null) throw new global::System.Text.Json.JsonException(" + message + ");");
        }
        foreach (
            var member in context
                .Contract.JsonMembers.Where(static contractMember => SparseChangeSetBasicsEmitter.IsNested(contractMember.Member))
                .Select(static contractMember => contractMember.Member)
        )
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var childType = member.ChildModel!.Value.NonNullableName;
            code.AppendLine(
                "if (value."
                    + property
                    + "?.State == "
                    + context.Runtime
                    + "ChangePayloadState.Value) "
                    + context.ConverterReference(childType)
                    + ".ValidateRootForJson(value."
                    + property
                    + ".Value);"
            );
        }
        code.CloseBlock();
    }
}
