using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Restores one member change from its payload variant.</summary>
/// <remarks>
/// Keeps the per-member restore case beside the payload reader so the DTO
/// emitter stays focused on shapes and the forward projection.
/// </remarks>
internal static class SparseChangeSetPayloadRestoreEmitter
{
    internal static void AppendFromPayloadCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        code.AppendLine("case " + variant + " item:");
        code.AppendLine(
            "if (__payloadSeen"
                + id
                + ") throw new global::System.ArgumentException(\"A payload cannot contain duplicate member changes.\", nameof(payload));"
        );
        code.AppendLine("__payloadSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLine(
                "if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));"
            );
            code.AppendLine("__payloadNested" + id + " = item.Nested.ToChangeSetCore();");
            code.AppendLine("break;");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            code.AppendLine("__payloadHas" + id + " = true;");
            code.AppendLine(
                "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
            );
            code.AppendLine("if (item.Before is not null || item.After is not null)");
            code.OpenBlock();
            code.AppendLine(
                "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
            );
            code.AppendLine("__payloadWhole" + id + " = true;");
            code.AppendLine("__payloadBefore" + id + " = item.Before.ToOptional();");
            code.AppendLine("__payloadAfter" + id + " = item.After.ToOptional();");
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "var __payloadSeenKeys"
                    + id
                    + " = new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">();"
            );
            var hasTempRestore = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
            if (hasTempRestore)
            {
                code.AppendLine(
                    "var __payloadSeenTemps"
                        + id
                        + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
                );
            }
            code.AppendLine("foreach (var changeItem in item.Items)");
            code.OpenBlock();
            code.AppendLine(
                "if (changeItem is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
            );
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                            member,
                            "changeItem.Key"
                        )
                        + " && !__payloadSeenKeys"
                        + id
                        + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
                );
            }
            else
            {
                code.AppendLine(
                    "if (!__payloadSeenKeys"
                        + id
                        + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
                );
            }
            code.AppendLine(
                "__payloadItems" + id + ".Add(__SparsePayloadItem" + id + "(changeItem));"
            );
            if (hasTempRestore)
            {
                // Temporary identities must be unique per collection.
                code.AppendLine(
                    "if (changeItem.TemporaryKey.HasValue && !__payloadSeenTemps"
                        + id
                        + ".Add(changeItem.TemporaryKey.Value)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate temporary keyed changes.\", nameof(payload));"
                );
            }
            code.CloseBlock();
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                code.AppendLine("__payloadBeforeOrder" + id + " = item.BeforeOrder;");
                code.AppendLine("__payloadAfterOrder" + id + " = item.AfterOrder;");
                if (hasTempRestore)
                {
                    // Temporary orders are explicit transport: temp-carrying
                    // items without them are malformed, never position-guessed.
                    code.AppendLine(
                        "var __hasTempItems"
                            + id
                            + " = false; foreach (var __ci in item.Items) if (__ci.TemporaryKey.HasValue) { __hasTempItems"
                            + id
                            + " = true; break; }"
                    );
                    code.AppendLine(
                        "if (item.TempBeforeOrder is null && __hasTempItems"
                            + id
                            + ") throw new global::System.ArgumentException(\"A temporary-keyed payload must contain temporary orders.\", nameof(payload));"
                    );
                    code.AppendLine(
                        "__payloadTempBeforeOrder"
                            + id
                            + " = item.TempBeforeOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                    );
                    code.AppendLine(
                        "if (item.TempAfterOrder is null && __hasTempItems"
                            + id
                            + ") throw new global::System.ArgumentException(\"A temporary-keyed payload must contain temporary orders.\", nameof(payload));"
                    );
                    code.AppendLine(
                        "__payloadTempAfterOrder"
                            + id
                            + " = item.TempAfterOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                    );
                }
            }
            code.CloseBlock();
            code.AppendLine("break;");
        }
        else
        {
            if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
            {
                code.AppendLine(
                    "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
                );
                code.AppendLine("__payloadHas" + id + " = true;");
                code.AppendLine("__payloadBefore" + id + " = item.Before.ToOptional();");
                code.AppendLine("__payloadAfter" + id + " = item.After.ToOptional();");
                code.AppendLine("break;");
            }
            else
            {
                // Transport policy: an undisclosed before-state can never
                // rebuild a complete ChangeSet.
                code.AppendLine(
                    "if (item.Before is not null) throw new global::System.ArgumentException(\"A redacted member must omit its before-state.\", nameof(payload));"
                );
                code.AppendLine(
                    "if (item.After is null) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(payload));"
                );
                code.AppendLine(
                    "throw new global::System.ArgumentException(\"Member '"
                        + member.Property.Name
                        + "' is redacted and cannot be converted to a complete ChangeSet. Use ToPatch() for the baseline-free projection.\", nameof(payload));"
                );
            }
        }
    }
}
