namespace SparseTypes.Generator.Shared;

/// <summary>Emits the mixed-partition case for keyed and dictionary payload members.</summary>
/// <remarks>Collection members convert per item.</remarks>
internal static class SparseChangeSetMixedKeyedEmitter
{
    internal static void AppendKeyedPartitionCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        int id,
        string esc,
        string path
    )
    {
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var collectionPatch = "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        code.AppendLine("__payloadHas" + id + " = true;");
        code.AppendLine(
            "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
        );
        // Per-item redacted endpoints cannot form item transitions; fail before
        // the shared item helper converts endpoints without naming the path.
        code.AppendLine("foreach (var __raw" + id + " in item.Items)");
        code.OpenBlock();
        code.AppendLine("if (__raw" + id + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));");
        // Value-free endpoints must not smuggle values past the redacted
        // branch below.
        code.AppendLine("__raw" + id + ".Before?.Validate(); __raw" + id + ".After?.Validate();");
        if (!member.RedactBefore)
        {
            code.AppendLine(
                "if (__raw"
                    + id
                    + ".Before is not null && __raw"
                    + id
                    + ".Before.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item before-state for '\" + "
                    + path
                    + " + \"[\" + (((object?)__raw"
                    + id
                    + ".Key is null) ? \"?\" : (object?)__raw"
                    + id
                    + ".Key) + \"]\" + \"'. Per-item redacted endpoints are not supported; send a whole-member blind set.\", nameof(payload));"
            );
        }
        code.AppendLine(
            "if (__raw"
                + id
                + ".After is not null && __raw"
                + id
                + ".After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item after-state for '\" + "
                + path
                + " + \"[\" + (((object?)__raw"
                + id
                + ".Key is null) ? \"?\" : (object?)__raw"
                + id
                + ".Key) + \"]\" + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.CloseBlock();
        code.AppendLine(
            "if (item.After is not null && item.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLine("if (item.Before is not null || item.After is not null)");
        code.OpenBlock();
        code.AppendLine(
            "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
        );
        code.AppendLine("item.Before.Validate(); item.After.Validate();");
        code.AppendLine("if (item.Before.IsRedacted)");
        code.OpenBlock();
        code.AppendLine("var __wholeAfter" + id + " = item.After.ToOptional();");
        code.AppendLine(
            "if (!__wholeAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"The payload redacts the before-state of whole collection member '\" + "
                + path
                + " + \"' with a missing after-state. Blind whole-collection removal has no patch projection; express the removal with an explicit patch.\", nameof(payload));"
        );
        code.AppendLine("var __wholeBlind" + id + " = new " + collectionPatch + "();");
        code.AppendLine("__wholeBlind" + id + ".Set(__wholeAfter" + id + ".Value!);");
        code.AppendLine("blindSets." + esc + " = __wholeBlind" + id + ";");
        code.AppendLine("blindPaths.Add(" + path + ");");
        code.AppendLine("__payloadHas" + id + " = false;");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("__payloadWhole" + id + " = true;");
        code.AppendLine("__payloadBefore" + id + " = item.Before.ToOptional();");
        code.AppendLine("__payloadAfter" + id + " = item.After.ToOptional();");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var __payloadSeenKeys" + id + " = new global::System.Collections.Generic.HashSet<" + keyType + ">();");
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLine("var __payloadSeenTemps" + id + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();");
        }
        // Redacted-before items pass requested after-states without comparison;
        // ordinary items retain baseline checks and strict validation.
        var isModelValue = isKeyed ? member.Collection.ElementType.IsFragmentModel : member.Collection.ValueType?.IsFragmentModel == true;
        var canContainBlindItems = isModelValue || member.RedactBefore;
        code.AppendLine("__payloadItems" + id + " = new(" + (canContainBlindItems ? string.Empty : "item.Items.Count") + ");");
        if (canContainBlindItems)
        {
            code.AppendLine("var __blindColl" + id + " = new " + collectionPatch + "();");
            code.AppendLine("bool __hasBlindColl" + id + " = false;");
        }
        code.AppendLine("foreach (var changeItem in item.Items)");
        code.OpenBlock();
        code.AppendLine("if (changeItem is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));");
        code.AppendLine("changeItem.Before?.Validate(); changeItem.After?.Validate();");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "changeItem.Key")
                    + " && !__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        else
        {
            code.AppendLine(
                "if (!__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary identities must be unique per collection, like keys.
            code.AppendLine(
                "if (changeItem.TemporaryKey.HasValue && !__payloadSeenTemps"
                    + id
                    + ".Add(changeItem.TemporaryKey.Value)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate temporary keyed changes.\", nameof(payload));"
            );
        }
        if (canContainBlindItems)
        {
            code.AppendLine(
                "var __itemPath" + id + " = " + path + " + \"[\" + (((object?)changeItem.Key is null) ? \"?\" : (object?)changeItem.Key) + \"]\";"
            );
        }
        if (isModelValue)
        {
            // Edited model items carry a nested core instead of endpoints.
            var valueChangeSet = isKeyed
                ? SparseChangeSetBasicsEmitter.ElementChangeSetOf(member)
                : SparseChangeSetBasicsEmitter.ValueChangeSetOf(member);
            code.AppendLine(valueChangeSet + "? __restoredEdit" + id + " = null;");
            code.AppendLine("if (changeItem.Edit is not null)");
            code.OpenBlock();
            code.AppendLine(
                "__restoredEdit"
                    + id
                    + " = "
                    + valueChangeSet
                    + ".__SparseMixedPartition(changeItem.Edit, __itemPath"
                    + id
                    + " + \".\", out var __editBlind"
                    + id
                    + ", out var __editPaths"
                    + id
                    + ");"
            );
            code.AppendLine("if (!__editBlind" + id + ".__SparseIsEmpty())");
            code.OpenBlock();
            // Only the blind portion passes through unchecked; the reversible
            // portion keeps its baseline checks and joins the payload items
            // below instead of collapsing into the blind patch.
            if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "if (changeItem.TemporaryKey.HasValue) __blindColl"
                        + id
                        + ".__SparseSetEditedByTemporaryKey(changeItem.TemporaryKey.Value, __editBlind"
                        + id
                        + "); else __blindColl"
                        + id
                        + ".__SparseSetEdited(changeItem.Key!, __editBlind"
                        + id
                        + ");"
                );
            }
            else
            {
                code.AppendLine("__blindColl" + id + ".__SparseSetEdited(changeItem.Key!, __editBlind" + id + ");");
            }
            code.AppendLine("blindPaths.AddRange(__editPaths" + id + ");");
            code.AppendLine("__hasBlindColl" + id + " = true;");
            code.AppendLine("if (__restoredEdit" + id + " is null || __restoredEdit" + id + ".IsEmpty) continue;");
            code.CloseBlock();
            code.CloseBlock();
        }
        if (member.RedactBefore)
        {
            var itemKind = dialect.RuntimeNamespace + "ChangePayloadItemKind";
            if (isModelValue)
            {
                // Removals may omit undisclosed before-values; edits must omit
                // endpoints and carry a payload. Avoid scalar-only Update APIs.
                code.AppendLine("if (changeItem.Before is not null && changeItem.Before.IsRedacted)");
                code.OpenBlock();
                code.AppendLine("if (changeItem.Kind == " + itemKind + ".Remove && changeItem.Edit is null)");
                code.OpenBlock();
                if (isKeyed)
                {
                    code.AppendLine("__blindColl" + id + ".Remove(changeItem.Key!);");
                }
                else
                {
                    code.AppendLine("__blindColl" + id + ".RemoveEntry(changeItem.Key!);");
                }
                code.AppendLine("blindPaths.Add(" + path + ");");
                code.AppendLine("__hasBlindColl" + id + " = true;");
                code.AppendLine("continue;");
                code.CloseBlock();
                code.AppendLine(
                    "if (changeItem.Kind == "
                        + itemKind
                        + ".Edit && changeItem.Edit is null && changeItem.After is not null && !changeItem.After.IsRedacted)"
                );
                code.OpenBlock();
                code.AppendLine("var __replacementAfter" + id + " = changeItem.After.ToOptional();");
                code.AppendLine(
                    "if (!__replacementAfter"
                        + id
                        + ".IsPresent) throw new global::System.ArgumentException(\"A model item replacement requires an observable after-state.\", nameof(payload));"
                );
                var itemPatchType = isKeyed
                    ? SparseKeyedCollectionEmitter.ElementPatchType(member)
                    : SparseKeyedCollectionEmitter.ValuePatchType(member);
                var itemFragmentType = isKeyed
                    ? SparseChangeSetBasicsEmitter.ElementFragmentOf(member)
                    : SparseChangeSetBasicsEmitter.ValueFragmentOf(member);
                code.AppendLine(
                    "var __replacementPatch"
                        + id
                        + " = new "
                        + itemPatchType
                        + "("
                        + itemFragmentType
                        + ".From(__replacementAfter"
                        + id
                        + ".Value!));"
                );
                if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine(
                        "if (changeItem.TemporaryKey.HasValue) __blindColl"
                            + id
                            + ".__SparseSetEditedByTemporaryKey(changeItem.TemporaryKey.Value, __replacementPatch"
                            + id
                            + "); else __blindColl"
                            + id
                            + ".__SparseSetEdited(changeItem.Key!, __replacementPatch"
                            + id
                            + ");"
                    );
                }
                else
                {
                    code.AppendLine("__blindColl" + id + ".__SparseSetEdited(changeItem.Key!, __replacementPatch" + id + ");");
                }
                code.AppendLine("blindPaths.Add(__itemPath" + id + ");");
                code.AppendLine("__hasBlindColl" + id + " = true;");
                code.AppendLine("continue;");
                code.CloseBlock();
                code.AppendLine(
                    "throw new global::System.ArgumentException(\"A redacted model item before-state is valid only for a removal or a replacement with an observable after-state.\", nameof(payload));"
                );
                code.CloseBlock();
            }
            else
            {
                var itemValueType = isKeyed ? SparseChangeSetBasicsEmitter.ElementTypeOf(member) : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
                var itemOptional = dialect.RuntimeNamespace + "Optional<" + itemValueType + ">";
                code.AppendLine("if (changeItem.Before is not null && changeItem.Before.IsRedacted)");
                code.OpenBlock();
                code.AppendLine(
                    "if (changeItem.Kind != "
                        + itemKind
                        + ".Edit && changeItem.Kind != "
                        + itemKind
                        + ".Remove) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' payload item with a redacted before-state must be an edit or a removal.\", nameof(payload));"
                );
                code.AppendLine(
                    "if (changeItem.Kind == "
                        + itemKind
                        + ".Edit && (changeItem.After is null || changeItem.After.IsRedacted)) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' blind edit must contain an observable after-state.\", nameof(payload));"
                );
                code.AppendLine(
                    "var __blindAfter" + id + " = changeItem.After is null ? " + itemOptional + ".Missing : changeItem.After.ToOptional();"
                );
                code.AppendLine(
                    "if (changeItem.Kind == "
                        + itemKind
                        + ".Edit && !__blindAfter"
                        + id
                        + ".IsPresent) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' blind edit must contain an after-state.\", nameof(payload));"
                );
                if (isKeyed)
                {
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine(
                            "if (changeItem.Kind == "
                                + itemKind
                                + ".Edit) __blindColl"
                                + id
                                + ".Update(__blindAfter"
                                + id
                                + ".Value!); else if (changeItem.TemporaryKey.HasValue) __blindColl"
                                + id
                                + ".RemoveByTemporaryKey(changeItem.TemporaryKey.Value); else __blindColl"
                                + id
                                + ".Remove(changeItem.Key!);"
                        );
                    }
                    else
                    {
                        code.AppendLine(
                            "if (changeItem.Kind == "
                                + itemKind
                                + ".Edit) __blindColl"
                                + id
                                + ".Update(__blindAfter"
                                + id
                                + ".Value!); else __blindColl"
                                + id
                                + ".Remove(changeItem.Key!);"
                        );
                    }
                }
                else
                {
                    code.AppendLine(
                        "if (changeItem.Kind == "
                            + itemKind
                            + ".Edit) __blindColl"
                            + id
                            + ".SetEntry(changeItem.Key!, __blindAfter"
                            + id
                            + ".Value!); else __blindColl"
                            + id
                            + ".RemoveEntry(changeItem.Key!);"
                    );
                }
                code.AppendLine("blindPaths.Add(" + path + ");");
                code.AppendLine("__hasBlindColl" + id + " = true;");
                code.AppendLine("continue;");
                code.CloseBlock();
            }
        }
        code.AppendLine(
            "__payloadItems" + id + ".Add(__SparsePayloadItem" + id + "(changeItem" + (isModelValue ? ", __restoredEdit" + id : string.Empty) + "));"
        );
        code.CloseBlock();
        if (canContainBlindItems)
        {
            code.AppendLine("if (__hasBlindColl" + id + ") blindSets." + esc + " = __blindColl" + id + ";");
        }
        if (isKeyed)
        {
            code.AppendLine("__payloadBeforeOrder" + id + " = item.BeforeOrder;");
            code.AppendLine("__payloadAfterOrder" + id + " = item.AfterOrder;");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                // Temporary orders restore parallel to the key orders; items
                // without them are malformed, never position-guessed.
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
                    "__payloadTempBeforeOrder" + id + " = item.TempBeforeOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                );
                code.AppendLine(
                    "if (item.TempAfterOrder is null && __hasTempItems"
                        + id
                        + ") throw new global::System.ArgumentException(\"A temporary-keyed payload must contain temporary orders.\", nameof(payload));"
                );
                code.AppendLine(
                    "__payloadTempAfterOrder" + id + " = item.TempAfterOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                );
            }
        }
        code.CloseBlock();
        code.AppendLine("break;");
    }
}
