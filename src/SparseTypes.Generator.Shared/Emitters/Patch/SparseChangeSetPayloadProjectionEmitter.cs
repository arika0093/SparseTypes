using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the baseline-free payload projection for redacted transports.</summary>
/// <remarks>A redacted payload can never rebuild a complete <c>ChangeSet</c>.</remarks>
internal static class SparseChangeSetPayloadProjectionEmitter
{
    internal static void AppendCoreToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
        {
            throw new ArgumentException("A payload projection requires a model type.", nameof(modelType));
        }

        SparseDownstreamPolicy.ThrowOnInvalidTransport(members, dialect);
        var runtime = dialect.RuntimeNamespace;
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Root");
        var prefix = SparseNaming.PatchApiPrefix(members.Select(static member => member.Property.Name));
        var strict = dialect.EffectiveRebasePolicy.RedactedBefore == SparseRedactedBeforeBehavior.StrictFail;
        code.AppendLine("/// <summary>Projects this payload to a baseline-free patch.</summary>");
        code.AppendLine("/// <returns>The baseline-free patch.</returns>");
        code.AppendLine("internal Patch ToPatchCore()");
        code.OpenBlock();
        code.AppendLine("if (TypedChange is null) throw new global::System.ArgumentException(\"A model change is required.\", nameof(TypedChange));");
        var modelChangeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        code.AppendLine("if (TypedChange.Kind == " + modelChangeKind + ".Replace)");
        code.OpenBlock();
        code.AppendLine("var whole = TypedChange;");
        code.AppendLine(
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(TypedChange));"
        );
        if (strict && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            code.AppendLine(
                "throw new global::System.InvalidOperationException(\"A redacted payload cannot be projected under the strict rebase policy.\");"
            );
        }
        else
        {
            AppendWholeProjection(code, runtime, payloadRoot, prefix);
        }
        code.CloseBlock();
        code.AppendLine("var patch = new Patch();");
        code.AppendLine("foreach (var change in EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendMemberProjection(code, member, members, dialect, modelType, strict);
        }

        code.AppendLine(
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(TypedChange));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return patch;");
        code.CloseBlock();
    }

    internal static void AppendRootToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
        {
            return;
        }

        if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
        {
            return;
        }

        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLine("/// <summary>Projects this payload to a baseline-free patch.</summary>");
        code.AppendLine(
            "/// <remarks>Redacted members project their requested after-state without historical comparison, unless the strict rebase policy applies.</remarks>"
        );
        code.AppendLine("/// <returns>The baseline-free patch.</returns>");
        code.AppendLine("public Patch ToPatch()");
        code.OpenBlock();
        code.AppendLine(
            "if (!global::System.String.Equals(Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangeSet payload version.\", nameof(Version));"
        );
        code.AppendLine("return ToPatchCore();");
        code.CloseBlock();
    }

    private static void AppendWholeProjection(SharedIndentedBuilder code, string runtime, string payloadRoot, string prefix)
    {
        code.AppendLine(
            "var __patchBeforeRoot = whole.Before is null ? " + runtime + "Optional<" + payloadRoot + "?>.Missing : whole.Before.ToOptional();"
        );
        code.AppendLine(
            "var __patchAfterRoot = whole.After is null ? " + runtime + "Optional<" + payloadRoot + "?>.Missing : whole.After.ToOptional();"
        );
        code.AppendLine(
            runtime
                + "Optional<Fragment?> __patchBefore = !__patchBeforeRoot.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(__patchBeforeRoot.Value?.ToFragment());"
        );
        code.AppendLine(
            runtime
                + "Optional<Fragment?> __patchAfter = !__patchAfterRoot.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(__patchAfterRoot.Value?.ToFragment());"
        );
        code.AppendLine("return Patch." + prefix + "Between(__patchBefore, __patchAfter);");
    }

    private static void AppendMemberProjection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        bool strict
    )
    {
        var id = member.Id;
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Change", id);
        code.AppendLine("case " + variant + " item:");
        if (IsNested(member))
        {
            code.AppendLine(
                "if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(TypedChange));"
            );
            code.AppendLine("patch." + name + " = item.Nested.ToPatchCore();");
            code.AppendLine("break;");
            return;
        }

        if (IsKeyed(member) || IsDict(member))
        {
            AppendKeyedProjection(code, member, members, dialect, modelType, name);
            return;
        }

        var runtime = dialect.RuntimeNamespace;
        var valueType = FragmentValueType(member);
        var operation = runtime + "FragmentOperation<" + valueType + ">";
        if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
        {
            code.AppendLine(
                "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(TypedChange));"
            );
            code.AppendLine("var __after" + id + " = item.After.ToOptional();");
            code.AppendLine(
                "patch." + name + " = __after" + id + ".IsPresent ? " + operation + ".Set(__after" + id + ".Value) : " + operation + ".Remove;"
            );
            code.AppendLine("break;");
            return;
        }

        // Transport policy: project the requested after-state without
        // historical comparison, unless the strict policy refuses instead.
        code.AppendLine(
            "if (item.Before is not null) throw new global::System.ArgumentException(\"A redacted member must omit its before-state.\", nameof(TypedChange));"
        );
        code.AppendLine(
            "if (item.After is null) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(TypedChange));"
        );
        code.AppendLine("var __redactedAfter" + id + " = item.After.ToOptional();");
        code.AppendLine(
            "if (!__redactedAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(TypedChange));"
        );
        if (strict)
        {
            code.AppendLine(
                "throw new global::System.InvalidOperationException(\"Member '"
                    + member.Property.Name
                    + "' is redacted and the strict rebase policy refuses projection.\");"
            );
        }
        else
        {
            code.AppendLine("patch." + name + " = " + operation + ".Set(__redactedAfter" + id + ".Value);");
        }
        code.AppendLine("break;");
    }

    private static void AppendKeyedProjection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string name
    )
    {
        var id = member.Id;
        var trans = TransNameFor(members, member);
        var runtime = dialect.RuntimeNamespace;
        var valueType = FragmentValueType(member);
        // Materialize canonical transition locals from the DTO, then reuse the
        // shared member projection so payload and ChangeSet paths cannot drift.
        code.AppendLine(
            "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(TypedChange));"
        );
        code.AppendLine("bool " + HasField(member) + " = true;");
        code.AppendLine("bool " + KeyedWholeFlag(member) + " = item.Before is not null || item.After is not null;");
        code.AppendLine(runtime + "Optional<" + valueType + "> " + KeyedWholeBefore(member) + " = default;");
        code.AppendLine(runtime + "Optional<" + valueType + "> " + KeyedWholeAfter(member) + " = default;");
        code.AppendLine(
            "global::System.Collections.Generic.List<" + modelType + ".ChangeSet." + trans + ".Item>? " + KeyedItems(member) + " = null;"
        );
        if (IsKeyed(member))
        {
            var keyType = KeyTypeOf(member);
            code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? " + KeyedBeforeOrder(member) + " = null;");
            code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? " + KeyedAfterOrder(member) + " = null;");
        }
        code.AppendLine("if (" + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(TypedChange));"
        );
        code.AppendLine(KeyedWholeBefore(member) + " = item.Before.ToOptional();");
        code.AppendLine(KeyedWholeAfter(member) + " = item.After.ToOptional();");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "var __payloadPatchItems" + id + " = new global::System.Collections.Generic.List<" + modelType + ".ChangeSet." + trans + ".Item>();"
        );
        code.AppendLine("foreach (var __payloadPatchDto" + id + " in item.Items)");
        code.OpenBlock();
        code.AppendLine(
            "if (__payloadPatchDto" + id + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(TypedChange));"
        );
        code.AppendLine("__payloadPatchItems" + id + ".Add(" + modelType + ".ChangeSet.__SparsePayloadItem" + id + "(__payloadPatchDto" + id + "));");
        code.CloseBlock();
        code.AppendLine(KeyedItems(member) + " = __payloadPatchItems" + id + ";");
        string? removalCount = null;
        if (IsKeyed(member))
        {
            code.AppendLine(KeyedBeforeOrder(member) + " = item.BeforeOrder;");
            code.AppendLine(KeyedAfterOrder(member) + " = item.AfterOrder;");
        }
        else
        {
            code.AppendLine("int __payloadPatchRemovals" + id + " = 0;");
            code.AppendLine("foreach (var __payloadPatchCount" + id + " in __payloadPatchItems" + id + ")");
            code.AppendLine("if (__payloadPatchCount" + id + ".IsRemoved) __payloadPatchRemovals" + id + "++;");
            removalCount = "__payloadPatchRemovals" + id;
        }
        code.CloseBlock();
        SparseChangeSetPatchSyncEmitter.AppendKeyedDictToPatch(code, member, name, dialect, removalCount);
        code.AppendLine("break;");
    }
}
