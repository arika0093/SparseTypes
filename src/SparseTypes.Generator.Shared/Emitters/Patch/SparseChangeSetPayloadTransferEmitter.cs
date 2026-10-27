using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the ChangeSet-to-payload transfer: envelope construction and root snapshots.</summary>
/// <remarks>Separated from the typed DTO definitions (<see cref="SparseChangeSetPayloadEmitter"/>).</remarks>
internal static class SparseChangeSetPayloadTransferEmitter
{
    internal static void AppendToPayloadCoreBridge(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget target
    )
    {
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        // The internal core projection stays callable in value form: nested
        // change-set values project through the facade bridge.
        code.AppendLine("/// <summary>Builds the transport core for this change set.</summary>");
        code.AppendLine(
            "/// <remarks>Changes to members marked with <c>SparseTransportIgnore</c> cannot be exported. JSON-ignored changes remain in the neutral payload but are rejected by the JSON codec.</remarks>"
        );
        code.AppendLine("/// <param name=\"redactBefores\">Whether to redact before-states.</param>");
        code.AppendLine("/// <returns>The transport core.</returns>");
        code.AppendLine(
            "internal "
                + payloadCore
                + " ToPayloadCore(bool redactBefores) => "
                + target.ChangeSetOperationsType
                + ".ToPayloadCore(this, redactBefores);"
        );
    }

    internal static void AppendToPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Root");
        var payloadModelChange = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChange");
        var payloadMembersDelta = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "MembersDelta");
        var payloadChangeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        if (target is not null)
        {
            code.AppendLine("/// <summary>Converts this change set into its serializable payload envelope.</summary>");
            code.AppendLine("/// <returns>The serializable payload envelope.</returns>");
            code.AppendLine("public ChangePayload ToPayload() => " + target.ChangeSetOperationsType + ".ToPayload(this);");
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Converts a change set into its serializable payload envelope.</summary>");
            code.AppendLine("/// <param name=\"self\">The change set to convert.</param>");
            code.AppendLine("/// <returns>The serializable payload envelope.</returns>");
            code.AppendLine("internal static ChangePayload ToPayload(ChangeSet self)");
            code.OpenBlock();
            AppendEmptyPayloadFastPath(
                code,
                payloadModelChange,
                payloadMembersDelta,
                payloadChangeKind,
                versionLiteral,
                "self.IsEmpty",
                "self.RecordedAt"
            );
            code.AppendLine(
                "return new ChangePayload { Version = " + versionLiteral + ", RecordedAt = self.RecordedAt, Change = ToPayloadChange(self, false) };"
            );
            code.CloseBlock();
        }
        else
        {
            code.AppendLine("/// <summary>Converts this change set into its serializable payload envelope.</summary>");
            code.AppendLine("/// <returns>The serializable payload envelope.</returns>");
            code.AppendLine("public ChangePayload ToPayload()");
            code.OpenBlock();
            AppendEmptyPayloadFastPath(code, payloadModelChange, payloadMembersDelta, payloadChangeKind, versionLiteral, "IsEmpty", "RecordedAt");
            code.AppendLine(
                "return new ChangePayload { Version = " + versionLiteral + ", RecordedAt = RecordedAt, Change = ToPayloadChange(false) };"
            );
            code.CloseBlock();
        }
        code.AppendLine("/// <summary>Builds the typed transport change for this change set.</summary>");
        code.AppendLine(
            "/// <remarks>Changes to members marked with <c>SparseTransportIgnore</c> cannot be exported. JSON-ignored changes remain in the neutral payload but are rejected by the JSON codec.</remarks>"
        );
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The change set to convert.</param>");
        }

        code.AppendLine("/// <param name=\"redactBefores\">Whether to redact before-states.</param>");
        code.AppendLine("/// <returns>The typed transport change.</returns>");
        code.AppendLine(
            (target is null ? "internal " : "internal static ")
                + payloadModelChange
                + " ToPayloadChange("
                + (target is null ? string.Empty : "ChangeSet self, ")
                + "bool redactBefores)"
        );
        code.OpenBlock();
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        SparseChangePayloadPatchSyncEmitter.AppendTransportIgnoreGuard(code, members);
        code.AppendLine(
            "var change = new " + payloadModelChange + " { Kind = " + payloadChangeKind + ".Update, Members = new " + payloadMembersDelta + "() };"
        );
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        if (SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            // Whole-root snapshots would leak undisclosed before-state through
            // a different path, so they are refused while policies apply.
            code.AppendLine(
                "throw new global::System.InvalidOperationException(\"A whole-root transition cannot be serialized while member transport policies apply.\");"
            );
        }
        else
        {
            code.AppendLine(
                "return new "
                    + payloadModelChange
                    + " { Kind = "
                    + payloadChangeKind
                    + ".Replace, Before = __SparsePayloadRoot(__sparse_wholeBefore, redactBefores), After = __SparsePayloadRootAfter(__sparse_wholeAfter) };"
            );
        }
        code.CloseBlock();

        SparseDownstreamPolicy.ThrowOnInvalidTransport(members, dialect);
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            var id = member.Id;
            var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var redact = member.RedactBefore ? "true" : "redactBefores";
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.NestedField(member) + " is not null)");
                code.OpenBlock();
                code.AppendLine(
                    "change.Members!."
                        + SparseNaming.EscapeIdentifier(member.Property.Name)
                        + " = new "
                        + variant
                        + " { Nested = "
                        + SparseChangeSetBasicsEmitter.NestedField(member)
                        + "!.ToPayloadCore("
                        + redact
                        + ") };"
                );
                code.CloseBlock();
            }
            else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
            {
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.HasField(member) + ")");
                code.OpenBlock();
                code.AppendLine("var entry = new " + variant + "();");
                // A redacted before-state stays undisclosed; the after-state
                // still travels so the change can be projected with ToPatch.
                var redactBefore = member.RedactBefore ? "true" : "redactBefores";
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.KeyedWholeFlag(member) + ")");
                code.OpenBlock();
                code.AppendLine(
                    "entry.Before = ("
                        + SparseChangeSetBasicsEmitter.KeyedWholeBefore(member)
                        + ".IsPresent && ("
                        + redactBefore
                        + ")) ? "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.Redacted() : "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.FromOptional("
                        + SparseChangeSetBasicsEmitter.KeyedWholeBefore(member)
                        + ");"
                );
                code.AppendLine(
                    "entry.After = "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.FromOptional("
                        + SparseChangeSetBasicsEmitter.KeyedWholeAfter(member)
                        + ");"
                );
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                var itemField = SparseChangeSetBasicsEmitter.KeyedItems(member);
                var itemValueType = SparseChangeSetBasicsEmitter.IsKeyed(member)
                    ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                    : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
                var isModelValue = SparseChangeSetBasicsEmitter.IsKeyed(member)
                    ? member.Collection.ElementType.IsFragmentModel
                    : member.Collection.ValueType?.IsFragmentModel == true;
                var omitItemEndpoints = "";
                if (isModelValue)
                {
                    omitItemEndpoints = SparseChangeSetBasicsEmitter.IsKeyed(member) ? "item.IsEdited || item.IsReordered || " : "item.IsEdited || ";
                }

                var beforeEndpoint =
                    "("
                    + omitItemEndpoints
                    + "!item.Before.IsPresent ? null : (("
                    + redactBefore
                    + ") ? "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.Redacted() : "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional(item.Before)))";
                var afterEndpoint =
                    "(" + omitItemEndpoints + "!item.After.IsPresent ? null : " + endpoint + "<" + itemValueType + ">.FromOptional(item.After))";
                code.AppendLine("entry.Items.Capacity = " + itemField + "?.Count ?? 0;");
                code.AppendLine("if (" + itemField + " is not null) foreach (var item in " + itemField + ")");
                code.OpenBlock();
                code.AppendLine(
                    "var mapped = new "
                        + SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Item", id)
                        + " { Key = item.Key, Before = "
                        + beforeEndpoint
                        + ", After = "
                        + afterEndpoint
                        + ", Kind = item.IsAdded ? "
                        + runtime
                        + "ChangePayloadItemKind.Add : item.IsRemoved ? "
                        + runtime
                        + "ChangePayloadItemKind.Remove : item.IsEdited ? "
                        + runtime
                        + "ChangePayloadItemKind.Edit : "
                        + runtime
                        + "ChangePayloadItemKind.Reorder };"
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    code.AppendLine("mapped.BeforeIndex = item.BeforeIndex;");
                    code.AppendLine("mapped.AfterIndex = item.AfterIndex;");
                    code.AppendLine("mapped.IsReordered = item.IsReordered;");
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine("mapped.TemporaryKey = item.TemporaryKey;");
                    }
                }
                if (isModelValue)
                {
                    code.AppendLine("if (item.IsEdited) mapped.Edit = item.Edit.ToPayloadCore(" + redactBefore + ");");
                }

                code.AppendLine("entry.Items.Add(mapped);");
                code.CloseBlock();
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    code.AppendLine("entry.BeforeOrder = " + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member) + ";");
                    code.AppendLine("entry.AfterOrder = " + SparseChangeSetBasicsEmitter.KeyedAfterOrder(member) + ";");
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine("entry.TempBeforeOrder = " + SparseChangeSetBasicsEmitter.KeyedTempBeforeOrder(member) + ";");
                        code.AppendLine("entry.TempAfterOrder = " + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member) + ";");
                    }
                }
                code.CloseBlock();
                code.AppendLine("change.Members!." + SparseNaming.EscapeIdentifier(member.Property.Name) + " = entry;");
                code.CloseBlock();
            }
            else
            {
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.HasField(member) + ")");
                code.OpenBlock();
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                var scalarRedact = member.RedactBefore ? "true" : "redactBefores";
                if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
                {
                    code.AppendLine(
                        "change.Members!."
                            + SparseNaming.EscapeIdentifier(member.Property.Name)
                            + " = new "
                            + variant
                            + " { Before = ("
                            + SparseChangeSetBasicsEmitter.BeforeField(member)
                            + ".IsPresent && ("
                            + scalarRedact
                            + ")) ? "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.Redacted() : "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.BeforeField(member)
                            + "), After = "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.AfterField(member)
                            + ") };"
                    );
                }
                else
                {
                    // Transport policy: the before-state stays undisclosed and
                    // the after-state travels alone. Never fabricate a missing.
                    code.AppendLine(
                        "change.Members!."
                            + SparseNaming.EscapeIdentifier(member.Property.Name)
                            + " = new "
                            + variant
                            + " { After = "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.AfterField(member)
                            + ") };"
                    );
                }
                code.CloseBlock();
            }
        }
        code.AppendLine("return change;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Builds the transport core for this change set.</summary>");
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The change set to convert.</param>");
        }

        code.AppendLine("/// <param name=\"redactBefores\">Whether to redact before-states.</param>");
        code.AppendLine("/// <returns>The transport core.</returns>");
        code.AppendLine(
            (target is null ? "internal " : "internal static ")
                + payloadCore
                + " ToPayloadCore("
                + (target is null ? string.Empty : "ChangeSet self, ")
                + "bool redactBefores) => new "
                + payloadCore
                + " { TypedChange = ToPayloadChange("
                + (target is null ? string.Empty : "self, ")
                + "redactBefores) };"
        );
        code.AppendLine();
        code.AppendLine(
            "private static " + endpoint + "<" + payloadRoot + "> __SparsePayloadRoot(" + runtime + "Optional<Fragment?> value, bool redactBefores)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (!value.IsPresent) return " + endpoint + "<" + payloadRoot + ">.FromOptional(" + runtime + "Optional<" + payloadRoot + ">.Missing);"
        );
        code.AppendLine(
            "if (value.Value is null) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present(null));"
        );
        // Endpoint-redact it so the mixed partition routes the whole change
        // blind instead of failing fragment conversion.
        var wholeBeforeRedacted = members.Any(static member => !member.Property.IsTransportIgnored && member.RedactBefore);
        if (wholeBeforeRedacted)
        {
            code.AppendLine("return " + endpoint + "<" + payloadRoot + ">.Redacted();");
        }
        else
        {
            code.AppendLine("if (redactBefores) return " + endpoint + "<" + payloadRoot + ">.Redacted();");
            code.AppendLine(
                "return "
                    + endpoint
                    + "<"
                    + payloadRoot
                    + ">.FromOptional("
                    + runtime
                    + "Optional<"
                    + payloadRoot
                    + ">.Present("
                    + payloadRoot
                    + ".FromFragment(value.Value)));"
            );
        }
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Builds an undisclosed-free after snapshot for a whole-root change.</summary>");
        code.AppendLine("private static " + endpoint + "<" + payloadRoot + "> __SparsePayloadRootAfter(" + runtime + "Optional<Fragment?> value)");
        code.OpenBlock();
        code.AppendLine(
            "if (!value.IsPresent) return " + endpoint + "<" + payloadRoot + ">.FromOptional(" + runtime + "Optional<" + payloadRoot + ">.Missing);"
        );
        code.AppendLine(
            "if (value.Value is null) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present(null));"
        );
        code.AppendLine(
            "return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present("
                + payloadRoot
                + ".FromFragment(value.Value)));"
        );
        code.CloseBlock();
    }

    private static void AppendEmptyPayloadFastPath(
        SharedIndentedBuilder code,
        string payloadModelChange,
        string payloadMembersDelta,
        string payloadChangeKind,
        string versionLiteral,
        string isEmptyExpression,
        string recordedAtExpression
    )
    {
        code.AppendLine("if (" + isEmptyExpression + ")");
        code.OpenBlock();
        code.AppendLine(
            "return new ChangePayload { Version = "
                + versionLiteral
                + ", Change = new "
                + payloadModelChange
                + " { Kind = "
                + payloadChangeKind
                + ".Update, Members = new "
                + payloadMembersDelta
                + "() }, RecordedAt = "
                + recordedAtExpression
                + " };"
        );
        code.CloseBlock();
    }
}
