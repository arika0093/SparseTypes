using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits ChangePayload patch synchronization for one model.</summary>
/// <remarks>A payload can contain transitions and baseline-free commands.</remarks>
internal static class SparseChangePayloadPatchSyncEmitter
{
    /// <summary>Emits the patch-side command core builder inside <c>Patch</c>.</summary>
    /// <remarks>Runs inside Patch so collection and operation internals stay accessible without widening their visibility.</remarks>
    internal static void AppendPatchToPayloadCore(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        if (modelType is null)
        {
            return;
        }

        if (target is not null)
        {
            var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
            code.AppendLine("/// <summary>Builds a baseline-free command core from this patch.</summary>");
            code.AppendLine(
                "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ChangePayload.ToPatch\"/>. Changes to members marked with <c>SparseTransportIgnore</c> cannot be exported.</remarks>"
            );
            code.AppendLine("/// <returns>The baseline-free command core.</returns>");
            code.AppendLine("internal " + core + " ToChangePayloadCore() => " + target.PatchOperationsType + ".ToChangePayloadCore(this);");
            code = target.PatchOperations;
        }
        AppendCoreFromPatch(code, members, dialect, modelType, implementationNamespace, target);
    }

    /// <summary>Emits the core-to-patch projection inside <c>ChangeSet</c>.</summary>
    internal static void AppendPatchFromCore(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        if (modelType is null)
        {
            return;
        }

        if (target is not null)
        {
            var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
            // The payload DTO bridges keep calling through the facade.
            code.AppendLine("/// <summary>Projects a validated command core to a baseline-free patch.</summary>");
            code.AppendLine("/// <param name=\"payload\">The validated command core.</param>");
            code.AppendLine("/// <returns>The baseline-free patch.</returns>");
            code.AppendLine(
                "internal static Patch PatchFromPayloadCore("
                    + core
                    + " payload) => "
                    + target.ChangeSetOperationsType
                    + ".PatchFromPayloadCore(payload);"
            );
            code = target.ChangeSetOperations;
        }
        AppendPatchFromCoreMethod(code, members, dialect, modelType);
    }

    private static void AppendCoreFromPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var modelChange = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChange");
        var membersDelta = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "MembersDelta");
        var modelChangeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        code.AppendLine("/// <summary>Builds a baseline-free command core from this patch.</summary>");
        code.AppendLine(
            "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ChangePayload.ToPatch\"/>. Changes to members marked with <c>SparseTransportIgnore</c> cannot be exported.</remarks>"
        );
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The patch to convert.</param>");
        }

        code.AppendLine("/// <returns>The baseline-free command core.</returns>");
        code.AppendLine(
            (target is null ? "internal " : "internal static ")
                + core
                + " ToChangePayloadCore("
                + (target is null ? string.Empty : "Patch self")
                + ")"
        );
        code.OpenBlock();
        if (target is not null)
        {
            SparseFragmentPatchCoreEmitter.AppendPatchSelfAliases(code, members, dialect);
        }
        AppendIgnoredPatchGuard(code, members, dialect);
        code.AppendLine("var change = new " + modelChange + " { Kind = " + modelChangeKind + ".Update, Members = new " + membersDelta + "() };");
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendCoreFromPatchMember(code, member, dialect, modelType, implementationNamespace);
        }

        code.AppendLine("return new " + core + " { TypedChange = change };");
        code.CloseBlock();
    }

    /// <summary>Emits the fail-closed guard for transport-ignored members.</summary>
    internal static void AppendTransportIgnoreGuard(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        var ignored = members.Where(static member => member.Property.IsTransportIgnored).ToArray();
        if (ignored.Length == 0)
        {
            return;
        }

        code.AppendLine("var __ignoredOmitted = new global::System.Collections.Generic.List<string>();");
        code.AppendLine("if (__sparse_hasWhole) __ignoredOmitted.Add(\"$root\");");
        foreach (var member in ignored)
        {
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.NestedField(member) + " is not null) __ignoredOmitted.Add(" + literal + ");");
            }
            else
            {
                code.AppendLine("if (" + SparseChangeSetBasicsEmitter.HasField(member) + ") __ignoredOmitted.Add(" + literal + ");");
            }
        }
        code.AppendLine(
            "if (__ignoredOmitted.Count != 0) throw new global::System.InvalidOperationException(\"ChangeSet transport excludes members marked with SparseTransportIgnore (\" + string.Join(\", \", __ignoredOmitted) + \").\");"
        );
    }

    /// <summary>Emits the fail-closed guard for transport-ignored patch fields.</summary>
    private static void AppendIgnoredPatchGuard(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var ignored = members.Where(static member => member.Property.IsTransportIgnored).ToArray();
        if (ignored.Length == 0)
        {
            return;
        }

        var kind = dialect.RuntimeNamespace + "FragmentOperationKind";
        code.AppendLine("var __ignoredOmitted = new global::System.Collections.Generic.List<string>();");
        foreach (var member in ignored)
        {
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true);
            var field = dialect.MemberField(member);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLine("if (" + field + " is not null && !" + field + ".__SparseIsEmpty()) __ignoredOmitted.Add(" + literal + ");");
            }
            else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
            {
                code.AppendLine("if (" + field + " is not null && !" + field + ".__SparseIsEmpty()) __ignoredOmitted.Add(" + literal + ");");
            }
            else
            {
                code.AppendLine("if (" + field + ".Kind != " + kind + ".Keep) __ignoredOmitted.Add(" + literal + ");");
            }
        }
        code.AppendLine(
            "if (__ignoredOmitted.Count != 0) throw new global::System.InvalidOperationException(\"Patch transport excludes members marked with SparseTransportIgnore (\" + string.Join(\", \", __ignoredOmitted) + \").\");"
        );
    }

    private static void AppendCoreFromPatchMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string? implementationNamespace = null
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "FragmentOperationKind";
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        var field = dialect.MemberField(member);
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childCore = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                dialect,
                member.ChildModel!.Value.NonNullableName,
                "Core",
                implementationNamespace
            );
            code.OpenBlock();
            code.AppendLine("var nestedPatch" + id + " = " + field + ";");
            code.AppendLine(
                "if (nestedPatch"
                    + id
                    + " is not null && !nestedPatch"
                    + id
                    + ".__SparseIsEmpty()) change.Members!."
                    + SparseNaming.EscapeIdentifier(member.Property.Name)
                    + " = new "
                    + change
                    + " { Nested = "
                    + childCore
                    + ".FromPatchCore(nestedPatch"
                    + id
                    + ") };"
            );
            code.CloseBlock();
        }
        else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            AppendCoreFromPatchCollection(code, dialect, modelType, id, field, SparseNaming.EscapeIdentifier(member.Property.Name));
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.OpenBlock();
            code.AppendLine("var operation" + id + " = " + field + ";");
            code.AppendLine("if (operation" + id + ".Kind != " + kind + ".Keep)");
            code.OpenBlock();
            code.AppendLine("var entry" + id + " = new " + change + "();");
            code.AppendLine("entry" + id + ".Before = " + endpoint + "<" + valueType + ">.Redacted();");
            code.AppendLine(
                "entry"
                    + id
                    + ".After = operation"
                    + id
                    + ".Kind == "
                    + kind
                    + ".Set ? "
                    + endpoint
                    + "<"
                    + valueType
                    + ">.FromOptional("
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(operation"
                    + id
                    + ".Value)) : new "
                    + endpoint
                    + "<"
                    + valueType
                    + "> { State = "
                    + state
                    + ".Missing };"
            );
            code.AppendLine("change.Members!." + SparseNaming.EscapeIdentifier(member.Property.Name) + " = entry" + id + ";");
            code.CloseBlock();
            code.CloseBlock();
        }
    }

    private static void AppendCoreFromPatchCollection(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        int id,
        string field,
        string propertyName
    )
    {
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        code.OpenBlock();
        code.AppendLine("var collection" + id + " = " + field + ";");
        code.AppendLine("if (collection" + id + " is not null && !collection" + id + ".__SparseIsEmpty())");
        code.OpenBlock();
        code.AppendLine("var entry" + id + " = new " + change + "();");
        code.AppendLine("collection" + id + ".__SparseExportChangePayload(entry" + id + ");");
        code.AppendLine("change.Members!." + propertyName + " = entry" + id + ";");
        code.CloseBlock();
        code.CloseBlock();
    }

    /// <summary>Emits the payload export helper inside a collection patch class.</summary>
    /// <remarks>Runs inside the collection patch so whole/item internals stay accessible without widening their visibility.</remarks>
    private static void AppendPatchFromCoreMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType
    )
    {
        var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var runtime = dialect.RuntimeNamespace;
        var state = runtime + "ChangePayloadState";
        code.AppendLine("/// <summary>Projects a validated command core to a baseline-free patch.</summary>");
        code.AppendLine("/// <remarks>Before-states are ignored rather than validated; malformed after-states are rejected.</remarks>");
        code.AppendLine("/// <param name=\"payload\">The validated command core.</param>");
        code.AppendLine("/// <returns>The baseline-free patch.</returns>");
        code.AppendLine("internal static Patch PatchFromPayloadCore(" + core + " payload)");
        code.OpenBlock();
        code.AppendLine("if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));");
        code.AppendLine(
            "if (payload.TypedChange is null) throw new global::System.ArgumentException(\"A model change is required.\", nameof(payload));"
        );
        code.AppendLine("var patch = new Patch();");
        var modelChangeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        code.AppendLine("if (payload.TypedChange.Kind == " + modelChangeKind + ".Replace)");
        code.OpenBlock();
        code.AppendLine("var whole = payload.TypedChange;");
        code.AppendLine(
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(payload));"
        );
        code.AppendLine(
            "if (whole.After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
        );
        code.AppendLine("var wholeAfter = __SparsePayloadFragment(whole.After);");
        code.AppendLine("if (!wholeAfter.IsPresent) { patch.__SparseRemove(); return patch; }");
        code.AppendLine("if (wholeAfter.Value is null) { patch.__SparseSetNull(); return patch; }");
        code.AppendLine("return new Patch(wholeAfter.Value);");
        code.CloseBlock();
        code.AppendLine("foreach (var change in payload.EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendPatchFromCoreMember(code, member, dialect, modelType, runtime, state);
        }

        code.AppendLine(
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(payload));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return patch;");
        code.CloseBlock();
    }

    private static void AppendPatchFromCoreMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string runtime,
        string state
    )
    {
        var id = member.Id;
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLine("case " + change + " entry" + id + ":");
            code.AppendLine(
                "if (entry" + id + ".Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));"
            );
            code.AppendLine("patch." + esc + " = entry" + id + ".Nested.ToPatchCore();");
            code.AppendLine("break;");
        }
        else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            AppendPatchFromCoreCollection(code, member, dialect, modelType, runtime, state, id, esc);
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            var operation = runtime + "FragmentOperation<" + valueType + ">";
            code.AppendLine("case " + change + " entry" + id + ":");
            code.AppendLine(
                "if (entry"
                    + id
                    + ".Before is null || entry"
                    + id
                    + ".After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
            );
            code.AppendLine(
                "if (entry"
                    + id
                    + ".After.State == "
                    + state
                    + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
            );
            code.AppendLine("var after" + id + " = entry" + id + ".After.ToOptional();");
            code.AppendLine(
                "patch." + esc + " = after" + id + ".IsPresent ? " + operation + ".Set(after" + id + ".Value) : " + operation + ".Remove;"
            );
            code.AppendLine("break;");
        }
    }

    private static void AppendPatchFromCoreCollection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string runtime,
        string state,
        int id,
        string esc
    )
    {
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        var collectionPatch = "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        code.AppendLine("case " + change + " entry" + id + ":");
        code.AppendLine(
            "if (entry"
                + id
                + ".Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
        );
        code.AppendLine("if (entry" + id + ".Before is not null || entry" + id + ".After is not null)");
        code.OpenBlock();
        code.AppendLine(
            "if (entry"
                + id
                + ".Before is null || entry"
                + id
                + ".After is null || entry"
                + id
                + ".Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
        );
        code.AppendLine(
            "if (entry"
                + id
                + ".After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
        );
        code.AppendLine("var wholeAfter" + id + " = entry" + id + ".After.ToOptional();");
        code.AppendLine("var whole" + id + " = new " + collectionPatch + "();");
        code.AppendLine("if (!wholeAfter" + id + ".IsPresent) whole" + id + ".Remove(); else whole" + id + ".Set(wholeAfter" + id + ".Value!);");
        code.AppendLine("patch." + esc + " = whole" + id + ";");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var collection" + id + " = new " + collectionPatch + "();");
        if (!isKeyed)
        {
            var itemKind = runtime + "ChangePayloadItemKind";
            code.AppendLine("int removeCount" + id + " = 0;");
            code.AppendLine("foreach (var reserveItem" + id + " in entry" + id + ".Items)");
            code.AppendLine(
                "if (reserveItem" + id + " is not null && reserveItem" + id + ".Kind == " + itemKind + ".Remove) removeCount" + id + "++;"
            );
            code.AppendLine("if (removeCount" + id + " > 0) collection" + id + ".__SparseReserveRemovals(removeCount" + id + ");");
        }
        if (isKeyed)
        {
            code.AppendLine("bool needsOrder" + id + " = false;");
        }

        code.AppendLine("foreach (var changeItem" + id + " in entry" + id + ".Items)");
        code.OpenBlock();
        AppendPatchFromCoreItem(code, member, runtime, state, id);
        code.CloseBlock();
        if (isKeyed)
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            code.AppendLine(
                "if (needsOrder"
                    + id
                    + " && entry"
                    + id
                    + ".AfterOrder is null) throw new global::System.ArgumentException(\"A reordered payload item requires an after-order.\", nameof(payload));"
            );
            code.AppendLine(
                "if (entry"
                    + id
                    + ".AfterOrder is not null && (entry"
                    + id
                    + ".BeforeOrder is null || !"
                    + dialect.RuntimeFacade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(entry"
                    + id
                    + ".BeforeOrder, entry"
                    + id
                    + ".AfterOrder))) collection"
                    + id
                    + ".SetOrder(entry"
                    + id
                    + ".AfterOrder);"
            );
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                // Temporary order restores parallel to the key order.
                code.AppendLine(
                    "if (entry"
                        + id
                        + ".TempAfterOrder is not null && entry"
                        + id
                        + ".AfterOrder is not null) { var __restoredOrderTemp = new global::System.Collections.Generic.List<global::System.Guid?>(); var __rti = 0; foreach (var __rok in entry"
                        + id
                        + ".AfterOrder) { if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__rok")
                        + ") { if (__rti >= entry"
                        + id
                        + ".TempAfterOrder.Count) throw new global::System.ArgumentException(\"A temporary-keyed payload has mismatched orders.\", nameof(payload)); __restoredOrderTemp.Add(entry"
                        + id
                        + ".TempAfterOrder[__rti++]); } else __restoredOrderTemp.Add(null); } if (__rti != entry"
                        + id
                        + ".TempAfterOrder.Count) throw new global::System.ArgumentException(\"A temporary-keyed payload has mismatched orders.\", nameof(payload)); collection"
                        + id
                        + ".__orderTemp = __restoredOrderTemp; }"
                );
            }
        }
        code.AppendLine("patch." + esc + " = collection" + id + ";");
        code.CloseBlock();
        code.AppendLine("break;");
    }

    private static void AppendPatchFromCoreItem(SharedIndentedBuilder code, SparseMemberModel member, string runtime, string state, int id)
    {
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var itemKind = runtime + "ChangePayloadItemKind";
        var isModelValue = isKeyed ? member.Collection.ElementType.IsFragmentModel : member.Collection.ValueType?.IsFragmentModel == true;
        var itemPatchType = string.Empty;
        var itemFragmentType = string.Empty;
        if (isModelValue)
        {
            itemPatchType = isKeyed ? SparseKeyedCollectionEmitter.ElementPatchType(member) : SparseKeyedCollectionEmitter.ValuePatchType(member);
            itemFragmentType = isKeyed
                ? SparseChangeSetBasicsEmitter.ElementFragmentOf(member)
                : SparseChangeSetBasicsEmitter.ValueFragmentOf(member);
        }
        code.AppendLine(
            "if (changeItem" + id + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        code.AppendLine(
            "if (changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Add && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Remove && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Edit && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Reorder) throw new global::System.ArgumentException(\"Unsupported payload item kind.\", nameof(payload));"
        );
        if (!isKeyed)
        {
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".Kind == "
                    + itemKind
                    + ".Reorder) throw new global::System.ArgumentException(\"Reorder is not supported for dictionary payload items.\", nameof(payload));"
            );
        }

        code.AppendLine("if (changeItem" + id + ".Kind == " + itemKind + ".Add)");
        code.OpenBlock();
        code.AppendLine(
            "if (changeItem"
                + id
                + ".Before is not null) throw new global::System.ArgumentException(\"An added payload item must omit 'before'.\", nameof(payload));"
        );
        code.AppendLine(
            "if (changeItem"
                + id
                + ".After is null || changeItem"
                + id
                + ".After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"An added payload item must contain an observable 'after'.\", nameof(payload));"
        );
        code.AppendLine("var added" + id + " = changeItem" + id + ".After.ToOptional();");
        code.AppendLine(
            "if (!added"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"An added payload item must contain 'after'.\", nameof(payload));"
        );
        if (isKeyed)
        {
            code.AppendLine("collection" + id + ".Add(added" + id + ".Value!);");
        }
        else
        {
            code.AppendLine("collection" + id + ".SetEntry(changeItem" + id + ".Key, added" + id + ".Value!);");
        }

        code.CloseBlock();
        code.AppendLine("else if (changeItem" + id + ".Kind == " + itemKind + ".Remove)");
        code.OpenBlock();
        code.AppendLine(
            "if (changeItem"
                + id
                + ".After is not null) throw new global::System.ArgumentException(\"A removed payload item must omit 'after'.\", nameof(payload));"
        );
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary removals route by Guid; sentinel keys would collide.
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".TemporaryKey.HasValue) collection"
                    + id
                    + ".RemoveByTemporaryKey(changeItem"
                    + id
                    + ".TemporaryKey.Value); else collection"
                    + id
                    + ".Remove(changeItem"
                    + id
                    + ".Key);"
            );
        }
        else if (isKeyed)
        {
            code.AppendLine("collection" + id + ".Remove(changeItem" + id + ".Key);");
        }
        else
        {
            code.AppendLine("collection" + id + ".RemoveEntry(changeItem" + id + ".Key);");
        }

        code.CloseBlock();
        code.AppendLine("else if (changeItem" + id + ".Kind == " + itemKind + ".Edit)");
        code.OpenBlock();
        if (isModelValue)
        {
            code.AppendLine("if (changeItem" + id + ".Edit is not null)");
            code.OpenBlock();
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".Before is not null || changeItem"
                    + id
                    + ".After is not null) throw new global::System.ArgumentException(\"A model item update must omit 'before' and 'after'.\", nameof(payload));"
            );
            if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "if (changeItem"
                        + id
                        + ".TemporaryKey.HasValue) collection"
                        + id
                        + ".__SparseSetEditedByTemporaryKey(changeItem"
                        + id
                        + ".TemporaryKey.Value, changeItem"
                        + id
                        + ".Edit.ToPatchCore()); else collection"
                        + id
                        + ".__SparseSetEdited(changeItem"
                        + id
                        + ".Key, changeItem"
                        + id
                        + ".Edit.ToPatchCore());"
                );
            }
            else
            {
                code.AppendLine("collection" + id + ".__SparseSetEdited(changeItem" + id + ".Key, changeItem" + id + ".Edit.ToPatchCore());");
            }
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".Before is null || changeItem"
                    + id
                    + ".After is null || changeItem"
                    + id
                    + ".After.IsRedacted) throw new global::System.ArgumentException(\"A model item replacement requires 'before' and an observable 'after'.\", nameof(payload));"
            );
            code.AppendLine("var replaced" + id + " = changeItem" + id + ".After.ToOptional();");
            code.AppendLine(
                "if (!replaced"
                    + id
                    + ".IsPresent) throw new global::System.ArgumentException(\"A model item replacement requires 'after'.\", nameof(payload));"
            );
            code.AppendLine("var replacedPatch" + id + " = new " + itemPatchType + "(" + itemFragmentType + ".From(replaced" + id + ".Value!));");
            if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLine(
                    "if (changeItem"
                        + id
                        + ".TemporaryKey.HasValue) collection"
                        + id
                        + ".__SparseSetEditedByTemporaryKey(changeItem"
                        + id
                        + ".TemporaryKey.Value, replacedPatch"
                        + id
                        + "); else collection"
                        + id
                        + ".__SparseSetEdited(changeItem"
                        + id
                        + ".Key, replacedPatch"
                        + id
                        + ");"
                );
            }
            else
            {
                code.AppendLine("collection" + id + ".__SparseSetEdited(changeItem" + id + ".Key, replacedPatch" + id + ");");
            }
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".After is null || changeItem"
                    + id
                    + ".After.State == "
                    + state
                    + ".Redacted) throw new global::System.ArgumentException(\"An edited scalar payload item must contain an observable 'after'.\", nameof(payload));"
            );
            code.AppendLine("var edited" + id + " = changeItem" + id + ".After.ToOptional();");
            code.AppendLine(
                "if (!edited"
                    + id
                    + ".IsPresent) throw new global::System.ArgumentException(\"An edited scalar payload item must contain 'after'.\", nameof(payload));"
            );
            if (isKeyed)
            {
                code.AppendLine("collection" + id + ".Update(edited" + id + ".Value!);");
            }
            else
            {
                code.AppendLine("collection" + id + ".UpdateEntry(changeItem" + id + ".Key, edited" + id + ".Value!);");
            }
        }
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        if (isKeyed)
        {
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".Before is not null || changeItem"
                    + id
                    + ".After is not null) throw new global::System.ArgumentException(\"A reordered payload item must omit 'before' and 'after'.\", nameof(payload));"
            );
            code.AppendLine(
                "if (changeItem"
                    + id
                    + ".Edit is not null) throw new global::System.ArgumentException(\"A reordered payload item must not contain 'edit'.\", nameof(payload));"
            );
            code.AppendLine("needsOrder" + id + " = true;");
        }
        code.CloseBlock();
    }
}
