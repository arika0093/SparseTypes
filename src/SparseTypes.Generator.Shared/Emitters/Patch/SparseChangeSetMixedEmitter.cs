using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the mixed-operation algebra for redacted/write-only payload members.</summary>
/// <remarks>Transport adapter, isolated from the complete ChangeSet algebra.</remarks>
internal static class SparseChangeSetMixedEmitter
{
    internal static void AppendMixedPartition(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        if (target is not null)
        {
            // Payload DTO bridges keep calling through the facade.
            code.AppendLine("/// <summary>Converts a validated root envelope to a change set.</summary>");
            code.AppendLine(
                "/// <exception cref=\"global::System.ArgumentException\">Thrown when the payload carries redacted before-states, which cannot form baseline-aware transitions.</exception>"
            );
            code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
            code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
            code.AppendLine(
                "internal static ChangeSet FromPayloadCore("
                    + payloadCore
                    + " payload) => "
                    + target.ChangeSetOperationsType
                    + ".FromPayloadCore(payload);"
            );
            code.AppendLine("/// <summary>Splits a validated envelope into baseline-aware transitions plus a blind patch.</summary>");
            code.AppendLine(
                "/// <remarks>Redacted before-states route to the blind patch without historical comparison; ordinary members keep baseline-aware validation. After-states must stay concrete.</remarks>"
            );
            code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
            code.AppendLine("/// <param name=\"pathPrefix\">The dotted path prefix.</param>");
            code.AppendLine("/// <param name=\"blindSets\">The blind patch for redacted paths.</param>");
            code.AppendLine("/// <param name=\"blindPaths\">The redacted paths routed to the blind patch.</param>");
            code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
            code.AppendLine(
                "internal static ChangeSet __SparseMixedPartition("
                    + payloadCore
                    + " payload, string pathPrefix, out Patch blindSets, out global::System.Collections.Generic.List<string> blindPaths) => "
                    + target.ChangeSetOperationsType
                    + ".__SparseMixedPartition(payload, pathPrefix, out blindSets, out blindPaths);"
            );
            code = target.ChangeSetOperations;
        }
        code.AppendLine("/// <summary>Converts a validated root envelope to a change set.</summary>");
        code.AppendLine(
            "/// <exception cref=\"global::System.ArgumentException\">Thrown when the payload carries redacted before-states, which cannot form baseline-aware transitions.</exception>"
        );
        code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
        code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
        code.AppendLine("internal static ChangeSet FromPayloadCore(" + payloadCore + " payload)");
        code.OpenBlock();
        code.AppendLine("if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));");
        code.AppendLine("var __changes = __SparseMixedPartition(payload, string.Empty, out var __blind, out var __blindPaths);");
        code.AppendLine(
            "if (!__blind.__SparseIsEmpty()) throw new global::System.ArgumentException(\"The payload contains redacted before-states (\" + string.Join(\", \", __blindPaths) + \") and cannot form a baseline-aware ChangeSet. Use ToPatch() for the baseline-free projection, or InvertReversibleChanges()/TryApplyMixedTo() for mixed requests.\", nameof(payload));"
        );
        code.AppendLine("return __changes;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Splits a validated envelope into baseline-aware transitions plus a blind patch.</summary>");
        code.AppendLine(
            "/// <remarks>Redacted before-states route to the blind patch without historical comparison; ordinary members keep baseline-aware validation. After-states must stay concrete.</remarks>"
        );
        code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
        code.AppendLine("/// <param name=\"pathPrefix\">The dotted path prefix.</param>");
        code.AppendLine("/// <param name=\"blindSets\">The blind patch for redacted paths.</param>");
        code.AppendLine("/// <param name=\"blindPaths\">The redacted paths routed to the blind patch.</param>");
        code.AppendLine("/// <returns>The baseline-aware change set.</returns>");
        code.AppendLine(
            "internal static ChangeSet __SparseMixedPartition("
                + payloadCore
                + " payload, string pathPrefix, out Patch blindSets, out global::System.Collections.Generic.List<string> blindPaths)"
        );
        code.OpenBlock();
        code.AppendLine("if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));");
        code.AppendLine("if (pathPrefix is null) throw new global::System.ArgumentNullException(nameof(pathPrefix));");
        code.AppendLine("blindSets = new Patch();");
        code.AppendLine("blindPaths = new global::System.Collections.Generic.List<string>();");
        code.AppendLine(
            "if (payload.TypedChange is null) throw new global::System.ArgumentException(\"A model change is required.\", nameof(payload));"
        );
        AppendWholePartition(code, members, dialect, modelType);
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendPartitionLocals(code, member, dialect, members);
        }
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            code.AppendLine("bool __payloadSeen" + member.Id + " = false;");
        }
        code.AppendLine("foreach (var change in payload.EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendPartitionCase(code, member, dialect, modelType);
        }
        code.AppendLine(
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(payload));"
        );
        code.CloseBlock();
        code.CloseBlock();
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            var id = member.Id;
            if (member.Property.IsTransportIgnored)
            {
                args.AddRange(SparseChangeSetBasicsEmitter.EmptyMemberArgs(member));
                continue;
            }
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                args.Add("__payloadNested" + id);
            }
            else if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
            {
                args.AddRange(
                    new[] { "__payloadHas" + id, "__payloadWhole" + id, "__payloadBefore" + id, "__payloadAfter" + id, "__payloadItems" + id }
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    args.Add("__payloadBeforeOrder" + id);
                    args.Add("__payloadAfterOrder" + id);
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        args.Add("__payloadTempBeforeOrder" + id);
                        args.Add("__payloadTempAfterOrder" + id);
                    }
                }
            }
            else
            {
                args.AddRange(new[] { "__payloadBefore" + id, "__payloadAfter" + id, "__payloadHas" + id });
            }
        }
        code.AppendLine("return new ChangeSet(" + string.Join(", ", args) + ");");
        code.CloseBlock();
    }

    private static void AppendPartitionLocals(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var id = member.Id;
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLine(SparseChangeSetBasicsEmitter.ChildChangeSet(member, dialect) + "? __payloadNested" + id + " = null;");
            return;
        }
        if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            var opt = runtime + "Optional<" + SparseChangeSetBasicsEmitter.FragmentValueType(member) + ">";
            var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
            code.AppendLine("bool __payloadHas" + id + " = false;");
            code.AppendLine("bool __payloadWhole" + id + " = false;");
            code.AppendLine(opt + " __payloadBefore" + id + " = default;");
            code.AppendLine(opt + " __payloadAfter" + id + " = default;");
            code.AppendLine("global::System.Collections.Generic.List<" + trans + ".Item>? __payloadItems" + id + " = null;");
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? __payloadBeforeOrder" + id + " = null;");
                code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? __payloadAfterOrder" + id + " = null;");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("global::System.Collections.Generic.List<global::System.Guid>? __payloadTempBeforeOrder" + id + " = null;");
                    code.AppendLine("global::System.Collections.Generic.List<global::System.Guid>? __payloadTempAfterOrder" + id + " = null;");
                }
            }
            return;
        }
        var scalarOpt = runtime + "Optional<" + SparseChangeSetBasicsEmitter.FragmentValueType(member) + ">";
        code.AppendLine("bool __payloadHas" + id + " = false;");
        code.AppendLine(scalarOpt + " __payloadBefore" + id + " = default;");
        code.AppendLine(scalarOpt + " __payloadAfter" + id + " = default;");
    }

    private static void AppendPartitionCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var prop = member.Property.Name;
        var propLit = SymbolDisplay.FormatLiteral(prop, true);
        var path = "pathPrefix + " + propLit;
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        var esc = SparseNaming.EscapeIdentifier(prop);
        if (SparseDownstreamPolicy.IsScalarMember(member) && dialect.GetTransport(prop) != SparseMemberTransport.Full)
        {
            // Member transport policy: an undisclosed before-state can never
            // rebuild a complete ChangeSet.
            SparseChangeSetPayloadRestoreEmitter.AppendFromPayloadCase(code, member, dialect, modelType);
            return;
        }
        code.AppendLine("case " + variant + " item:");
        code.AppendLine(
            "if (__payloadSeen"
                + id
                + ") throw new global::System.ArgumentException(\"A payload cannot contain duplicate member changes.\", nameof(payload));"
        );
        code.AppendLine("__payloadSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childChangeSet = SparseChangeSetBasicsEmitter.ChildChangeSet(member, dialect);
            code.AppendLine("if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));");
            code.AppendLine(
                "var __nested"
                    + id
                    + " = "
                    + childChangeSet
                    + ".__SparseMixedPartition(item.Nested, "
                    + path
                    + " + \".\", out var __nestedBlind"
                    + id
                    + ", out var __nestedPaths"
                    + id
                    + ");"
            );
            code.AppendLine("__payloadNested" + id + " = __nested" + id + ";");
            code.AppendLine("if (!__nestedBlind" + id + ".__SparseIsEmpty()) blindSets." + esc + " = __nestedBlind" + id + ";");
            code.AppendLine("blindPaths.AddRange(__nestedPaths" + id + ");");
            code.AppendLine("break;");
            return;
        }
        if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            SparseChangeSetMixedKeyedEmitter.AppendKeyedPartitionCase(code, member, dialect, id, esc, path);
            return;
        }
        var operation = runtime + "FragmentOperation<" + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member) + ">";
        code.AppendLine(
            "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
        );
        // A redacted endpoint is value-free by contract; validate before the
        // redacted branch bypasses ToOptional.
        code.AppendLine("item.Before.Validate(); item.After.Validate();");
        code.AppendLine(
            "if (item.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLine("if (item.Before.IsRedacted)");
        code.OpenBlock();
        code.AppendLine("var __blindAfter" + id + " = item.After.ToOptional();");
        code.AppendLine(
            "blindSets."
                + esc
                + " = __blindAfter"
                + id
                + ".IsPresent ? "
                + operation
                + ".Set(__blindAfter"
                + id
                + ".Value) : "
                + operation
                + ".Remove;"
        );
        code.AppendLine("blindPaths.Add(" + path + ");");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("__payloadHas" + id + " = true;");
        code.AppendLine("__payloadBefore" + id + " = item.Before.ToOptional();");
        code.AppendLine("__payloadAfter" + id + " = item.After.ToOptional();");
        code.CloseBlock();
        code.AppendLine("break;");
    }

    private static void AppendWholePartition(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var changeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        code.AppendLine("if (payload.TypedChange!.Kind == " + changeKind + ".Replace)");
        code.OpenBlock();
        code.AppendLine("var whole = payload.TypedChange!;");
        code.AppendLine(
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(payload));"
        );
        code.AppendLine("whole.Before.Validate(); whole.After.Validate();");
        if (SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            // A whole-root snapshot cannot prove redacted paths complete.
            code.AppendLine(
                "throw new global::System.ArgumentException(\"A redacted payload cannot be converted to a complete ChangeSet. Use ToPatch() for the baseline-free projection.\", nameof(payload));"
            );
        }
        code.AppendLine(
            "if (whole.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + pathPrefix + \"$root\" + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLine("if (whole.Before.IsRedacted)");
        code.OpenBlock();
        code.AppendLine("var __wholeAfter = whole.After.ToOptional();");
        code.AppendLine("if (!__wholeAfter.IsPresent) blindSets.__SparseRemove();");
        code.AppendLine("else if (__wholeAfter.Value is null) blindSets.__SparseSetNull();");
        code.AppendLine("else blindSets.__SparseSet(__wholeAfter.Value.ToFragment().ToModel());");
        code.AppendLine("blindPaths.Add(pathPrefix + \"$root\");");
        code.AppendLine("return new ChangeSet(false, default, default, " + SparseChangeSetBasicsEmitter.MemberEmptyTail(members) + ");");
        code.CloseBlock();
        code.AppendLine(
            "return new ChangeSet(true, __SparsePayloadFragment(whole.Before), __SparsePayloadFragment(whole.After), "
                + SparseChangeSetBasicsEmitter.MemberEmptyTail(members)
                + ");"
        );
        code.CloseBlock();
    }

    /// <summary>Emits the envelope version guard for public payload entry points.</summary>
    /// <remarks>The root envelope validates wire versions before delegation; nested cores remain versionless.</remarks>
    internal static void AppendVersionGuard(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect)
    {
        var versionLiteral = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLine(
            "if (!global::System.String.Equals(Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangePayload version.\", nameof(Version));"
        );
        code.AppendLine(
            "if (!IsValidSchemaIdentity(Schema)) throw new global::System.ArgumentException(\"Malformed ChangePayload schema identity.\", nameof(Schema));"
        );
    }

    /// <summary>Emits version-validated envelope overrides for mixed entry points.</summary>
    /// <remarks>The envelope re-declares the core seam so <c>ToPatch</c>.</remarks>
    internal static void AppendEnvelopeVersionOverrides(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType
    )
    {
        code.AppendLine("/// <summary>Inverts the reversible transitions and reports write-only paths.</summary>");
        code.AppendLine("/// <remarks>Validates the envelope wire version before interpreting the payload.</remarks>");
        code.AppendLine("/// <param name=\"skippedPaths\">The write-only paths excluded from the result.</param>");
        code.AppendLine("/// <returns>The inverted change set.</returns>");
        code.AppendLine(
            "public new "
                + modelType
                + ".ChangeSet InvertReversibleChanges(out global::System.Collections.Generic.IReadOnlyList<string> skippedPaths)"
        );
        code.OpenBlock();
        AppendVersionGuard(code, dialect);
        code.AppendLine("return base.InvertReversibleChanges(out skippedPaths).WithRecordedAt(RecordedAt);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Applies a mixed request to an ordinary model without committing a subset on conflict.</summary>");
        code.AppendLine("/// <remarks>Validates the envelope wire version before interpreting the payload.</remarks>");
        code.AppendLine("/// <param name=\"current\">The current model.</param>");
        code.AppendLine("/// <param name=\"updated\">The updated model when applied.</param>");
        code.AppendLine("/// <param name=\"result\">The apply outcome.</param>");
        code.AppendLine("/// <returns><see langword=\"true\"/> when the request was applied.</returns>");
        code.AppendLine(
            "public new bool TryApplyMixedTo("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out MixedApplyResult result)"
        );
        code.OpenBlock();
        AppendVersionGuard(code, dialect);
        code.AppendLine("return base.TryApplyMixedTo(current, out updated, out result);");
        code.CloseBlock();
    }

    internal static void AppendMixedPayloadSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames
    )
    {
        var prefix = SparseNaming.PatchApiPrefix(members.Select(static member => member.Property.Name));
        code.AppendLine("/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>");
        code.AppendLine(
            "/// <remarks>Redacted before-states project to their requested after-state without historical comparison; ordinary members project their after-state too. The result is baseline-free and can no longer rebase or report conflicts. Public callers enter through the version-validated envelope override.</remarks>"
        );
        code.AppendLine("/// <returns>The baseline-free patch.</returns>");
        code.AppendLine("public Patch ToPatch()");
        code.OpenBlock();
        code.AppendLine("var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out var __blind, out _);");
        code.AppendLine("return __changes.ToPatch()." + prefix + "Compose(__blind);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Inverts the reversible transitions and reports write-only paths.</summary>");
        code.AppendLine(
            "/// <remarks>Write-only operations have no prior value to restore, so they are excluded. A non-empty <paramref name=\"skippedPaths\"/> means the result is not a complete inverse; use <c>ChangeSet.Invert()</c> on complete change sets for true inversion.</remarks>"
        );
        code.AppendLine("/// <param name=\"skippedPaths\">The write-only paths excluded from the result.</param>");
        code.AppendLine("/// <returns>The inverted change set.</returns>");
        code.AppendLine("public ChangeSet InvertReversibleChanges(out global::System.Collections.Generic.IReadOnlyList<string> skippedPaths)");
        code.OpenBlock();
        code.AppendLine("var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out _, out var __blindPaths);");
        code.AppendLine("skippedPaths = __blindPaths.ToArray();");
        code.AppendLine("return __changes.Invert();");
        code.CloseBlock();
        if (modelType is null)
        {
            return;
        }
        AppendMixedApplyOutcome(code, dialect);
        AppendTryApplyMixed(code, members, dialect, modelType, prefix, ignoredSettablePropertyNames);
    }

    private static void AppendMixedApplyOutcome(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect)
    {
        var conflict = dialect.ConflictType;
        code.AppendLine("/// <summary>The outcome of applying a mixed request to an ordinary model.</summary>");
        code.AppendLine("public sealed class MixedApplyResult");
        code.OpenBlock();
        // Public-first order: public properties precede internal construction
        // helpers.
        code.AppendLine("/// <summary>Whether rebasing the baseline-aware members produced conflicts.</summary>");
        code.AppendLine("public bool HasConflicts => Conflicts.Count != 0;");
        code.AppendLine("/// <summary>Structured conflicts for baseline-aware members only.</summary>");
        code.AppendLine("public global::System.Collections.Generic.IReadOnlyList<" + conflict + "> Conflicts { get; }");
        code.AppendLine("/// <summary>Write-only member paths applied without baseline comparison.</summary>");
        code.AppendLine("public global::System.Collections.Generic.IReadOnlyList<string> WriteOnlyPaths { get; }");
        code.AppendLine(
            "internal MixedApplyResult(global::System.Collections.Generic.IReadOnlyList<"
                + conflict
                + "> conflicts, global::System.Collections.Generic.IReadOnlyList<string> writeOnlyPaths)"
        );
        code.OpenBlock();
        code.AppendLine("Conflicts = conflicts;");
        code.AppendLine("WriteOnlyPaths = writeOnlyPaths;");
        code.CloseBlock();
        code.AppendLine(
            "internal static MixedApplyResult Applied(global::System.Collections.Generic.List<string> writeOnlyPaths) => new(global::System.Array.Empty<"
                + conflict
                + ">(), writeOnlyPaths.ToArray());"
        );
        code.AppendLine(
            "internal static MixedApplyResult Conflicted(global::System.Collections.Generic.IReadOnlyList<"
                + conflict
                + "> conflicts, global::System.Collections.Generic.List<string> writeOnlyPaths) => new(conflicts, writeOnlyPaths.ToArray());"
        );
        code.CloseBlock();
    }

    private static void AppendTryApplyMixed(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string prefix,
        ImmutableArray<string> ignoredSettablePropertyNames
    )
    {
        _ = members;
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        code.AppendLine("/// <summary>Applies a mixed request to an ordinary model without committing a subset on conflict.</summary>");
        code.AppendLine(
            "/// <remarks>Baseline-aware members rebase onto the current model; redacted-before members pass their requested after-state through. When rebasing conflicts, nothing is applied and the current model is left untouched. This default passes write-only operations through; it does not claim conflict reconciliation, and revision checks stay with the application.</remarks>"
        );
        code.AppendLine("/// <param name=\"current\">The current model.</param>");
        code.AppendLine("/// <param name=\"updated\">The updated model when applied.</param>");
        code.AppendLine("/// <param name=\"result\">The apply outcome.</param>");
        code.AppendLine("/// <returns><see langword=\"true\"/> when the request was applied.</returns>");
        code.AppendLine(
            "public bool TryApplyMixedTo("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out MixedApplyResult result)"
        );
        code.OpenBlock();
        code.AppendLine("var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out var __blind, out var __blindPaths);");
        code.AppendLine("var __state = " + optionalFragment + ".Present(Fragment.From(current));");
        code.AppendLine("ChangeSet __toApply;");
        code.AppendLine("if (__changes.__SparseBeforeMatches(__state))");
        code.OpenBlock();
        code.AppendLine("__toApply = __changes;");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var __rebase = __changes.RebaseOnto(__state);");
        code.AppendLine("if (__rebase.HasConflicts)");
        code.OpenBlock();
        code.AppendLine("updated = null;");
        code.AppendLine("result = MixedApplyResult.Conflicted(__rebase.Conflicts, __blindPaths);");
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine("__toApply = __rebase.Rebased;");
        code.CloseBlock();
        code.AppendLine("var __applied = __toApply.ToPatch()." + prefix + "Compose(__blind).Apply(__state);");
        code.AppendLine(
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"Advancing the baseline did not produce a valid model state.\");"
        );
        code.AppendLine("var __updatedModel = __applied.Value.ToModel();");
        foreach (var ignoredName in ignoredSettablePropertyNames.IsDefault ? ImmutableArray<string>.Empty : ignoredSettablePropertyNames)
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLine("__updatedModel." + name + " = current." + name + ";");
        }
        code.AppendLine("updated = __updatedModel;");
        code.AppendLine("result = MixedApplyResult.Applied(__blindPaths);");
        code.AppendLine("return true;");
        code.CloseBlock();
        _ = runtime;
    }
}
