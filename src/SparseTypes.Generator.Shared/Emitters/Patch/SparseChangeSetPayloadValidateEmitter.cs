using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the serializer-independent structural payload validation.</summary>
/// <remarks>Validation mirrors <c>__SparseMixedPartition</c> acceptance without
/// throwing: blind and redacted envelopes are valid, and every routine structural
/// problem becomes a typed error. Conversion and rebase conflicts stay separate.</remarks>
internal static class SparseChangeSetPayloadValidateEmitter
{
    internal static void AppendCoreValidate(
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

        var runtime = dialect.RuntimeNamespace;
        var result = runtime + "ChangePayloadValidationResult";
        var error = runtime + "ChangePayloadValidationError";
        var kind = runtime + "ChangePayloadValidationKind";
        var errors = "global::System.Collections.Generic.List<" + error + ">";
        code.AppendLine("/// <summary>Validates the structural wire contract without converting or applying.</summary>");
        code.AppendLine(
            "/// <remarks>Blind and redacted envelopes are valid; a valid payload can still be unconvertible to a complete <c>ChangeSet</c>, and rebase conflicts are reported by <c>TryApplyMixedTo</c>, not here. Messages carry paths and reasons only, never secret values.</remarks>"
        );
        code.AppendLine("/// <returns>The validation outcome in deterministic payload order.</returns>");
        code.AppendLine("public " + result + " Validate()");
        code.OpenBlock();
        code.AppendLine("var __validateErrors = new " + errors + "();");
        code.AppendLine("__SparseValidateInto(__validateErrors, string.Empty);");
        code.AppendLine("return new " + result + "(__validateErrors);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Collects structural errors with the given path prefix.</summary>");
        code.AppendLine(
            "/// <remarks>Accepts the same envelopes as <c>__SparseMixedPartition</c>; nested cores and item edits recurse with extended prefixes.</remarks>"
        );
        code.AppendLine("/// <param name=\"errors\">The collected errors.</param>");
        code.AppendLine("/// <param name=\"pathPrefix\">The dotted path prefix.</param>");
        code.AppendLine("internal void __SparseValidateInto(" + errors + " errors, string pathPrefix)");
        code.OpenBlock();
        code.AppendLine("if (errors is null) throw new global::System.ArgumentNullException(nameof(errors));");
        code.AppendLine("if (pathPrefix is null) throw new global::System.ArgumentNullException(nameof(pathPrefix));");
        code.AppendLine(
            "if (TypedChange is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = pathPrefix, Message = \"A model change is required.\" }); return; }"
        );
        AppendWholeValidate(code, members, dialect, modelType, runtime, error, kind);
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            code.AppendLine("bool __validateSeen" + member.Id + " = false;");
        }
        code.AppendLine("foreach (var change in EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            AppendMemberValidate(code, member, dialect, modelType, runtime, error, kind);
        }
        code.AppendLine(
            "default: errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = pathPrefix, Message = \"Payload contains a null or unknown change variant.\" }); break;"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendEnvelopeValidate(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect, string modelType)
    {
        var runtime = dialect.RuntimeNamespace;
        var result = runtime + "ChangePayloadValidationResult";
        var error = runtime + "ChangePayloadValidationError";
        var kind = runtime + "ChangePayloadValidationKind";
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLine("/// <summary>Validates the envelope wire contract without converting or applying.</summary>");
        code.AppendLine("/// <remarks>Validates the envelope wire version before interpreting the payload.</remarks>");
        code.AppendLine("/// <returns>The validation outcome in deterministic payload order.</returns>");
        code.AppendLine("public new " + result + " Validate()");
        code.OpenBlock();
        code.AppendLine(
            "if (!global::System.String.Equals(Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) return new "
                + result
                + "(new "
                + error
                + "[] { new "
                + error
                + " { Kind = "
                + kind
                + ".Version, Path = string.Empty, Message = \"Unsupported ChangePayload version.\" } });"
        );
        code.AppendLine("return base.Validate();");
        code.CloseBlock();
    }

    private static void AppendWholeValidate(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string error,
        string kind
    )
    {
        _ = members;
        _ = dialect;
        _ = runtime;
        var changeKind = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");
        var rootPath = "pathPrefix + \"$root\"";
        code.AppendLine("if (TypedChange!.Kind == " + changeKind + ".Replace)");
        code.OpenBlock();
        code.AppendLine("var whole = TypedChange!;");
        code.AppendLine(
            "if (whole.Before is null || whole.After is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + rootPath
                + ", Message = \"A whole-root payload must contain both endpoints.\" }); return; }"
        );
        // Value-free endpoints must not smuggle values; reuse the endpoint
        // check so validation and conversion cannot disagree.
        AppendEndpointValidate(code, "whole.Before", rootPath, error, kind, "errors", "__validateWholeBeforeBad");
        AppendEndpointValidate(code, "whole.After", rootPath, error, kind, "errors", "__validateWholeAfterBad");
        code.AppendLine("if (__validateWholeBeforeBad || __validateWholeAfterBad) return;");
        code.AppendLine(
            "if (whole.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = \"The payload contains a redacted after-state for '\" + "
                + rootPath
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); return; }"
        );
        code.AppendLine("if (whole.Before.IsRedacted)");
        code.OpenBlock();
        // A blind whole-root set is valid; only its conversion shape is
        // checked, reusing the endpoint conversion.
        code.AppendLine("try { whole.After.ToOptional(); }");
        code.AppendLine(
            "catch (global::System.InvalidOperationException __validateWholeBlindEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = __validateWholeBlindEx.Message }); }"
        );
        code.AppendLine("return;");
        code.CloseBlock();
        // An ordinary whole-root snapshot converts through the same fragment
        // reader as the partition, reusing its member checks.
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("var __validateWholeBefore = whole.Before.ToOptional();");
        code.AppendLine("var __validateWholeAfter = whole.After.ToOptional();");
        code.AppendLine("if (__validateWholeBefore.IsPresent) __validateWholeBefore.Value?.ToFragment();");
        code.AppendLine("if (__validateWholeAfter.IsPresent) __validateWholeAfter.Value?.ToFragment();");
        code.CloseBlock();
        code.AppendLine(
            "catch (global::System.ArgumentException __validateWholeEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + rootPath
                + ", Message = __validateWholeEx.Message }); }"
        );
        code.AppendLine(
            "catch (global::System.InvalidOperationException __validateWholeEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = __validateWholeEx.Message }); }"
        );
        code.AppendLine("return;");
        code.CloseBlock();
    }

    private static void AppendMemberValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string error,
        string kind
    )
    {
        var id = member.Id;
        var propLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var path = "pathPrefix + " + propLit;
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", id);
        code.AppendLine("case " + variant + " item:");
        code.AppendLine(
            "if (__validateSeen"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Duplicate, Path = "
                + path
                + ", Message = \"A payload cannot contain duplicate member changes.\" }); break; }"
        );
        code.AppendLine("__validateSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLine(
                "if (item.Nested is null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Changes, Path = "
                    + path
                    + ", Message = \"Nested payload is required.\" }); break; }"
            );
            code.AppendLine("item.Nested.__SparseValidateInto(errors, " + path + " + \".\");");
            code.AppendLine("break;");
            return;
        }
        if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            SparseChangeSetPayloadValidateKeyedEmitter.AppendKeyedValidate(code, member, dialect, modelType, path, runtime, error, kind);
            return;
        }
        if (SparseDownstreamPolicy.IsScalarMember(member) && dialect.GetTransport(member.Property.Name) != SparseMemberTransport.Full)
        {
            // Transport policy mirrors the restore seam: an undisclosed
            // before-state can never rebuild a complete ChangeSet.
            code.AppendLine(
                "if (item.Before is not null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"A redacted member must omit its before-state.\" }); break; }"
            );
            code.AppendLine(
                "if (item.After is null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"A redacted member requires an after-state.\" }); break; }"
            );
            code.AppendLine(
                "errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"Member '"
                    + member.Property.Name
                    + "' is redacted and cannot be converted to a complete ChangeSet. Use ToPatch() for the baseline-free projection.\" });"
            );
            code.AppendLine("break;");
            return;
        }
        AppendScalarValidate(code, member, path, error, kind);
    }

    private static void AppendScalarValidate(SharedIndentedBuilder code, SparseMemberModel member, string path, string error, string kind)
    {
        var id = member.Id;
        code.AppendLine(
            "if (item.Before is null || item.After is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = \"Both transition endpoints are required.\" }); break; }"
        );
        AppendEndpointValidate(code, "item.Before", path, error, kind, "errors", "__validateBeforeBad" + id);
        AppendEndpointValidate(code, "item.After", path, error, kind, "errors", "__validateAfterBad" + id);
        code.AppendLine("if (__validateBeforeBad" + id + " || __validateAfterBad" + id + ") break;");
        code.AppendLine(
            "if (item.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = \"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); break; }"
        );
        code.AppendLine("if (item.Before.IsRedacted)");
        code.OpenBlock();
        // A blind scalar set is valid; only its conversion shape is checked.
        code.AppendLine("try { item.After.ToOptional(); }");
        code.AppendLine(
            "catch (global::System.InvalidOperationException __validateBlindEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateBlindEx"
                + id
                + ".Message }); }"
        );
        code.AppendLine("break;");
        code.CloseBlock();
        code.AppendLine("try { item.Before.ToOptional(); item.After.ToOptional(); }");
        code.AppendLine(
            "catch (global::System.InvalidOperationException __validateOrdinaryEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateOrdinaryEx"
                + id
                + ".Message }); }"
        );
        code.AppendLine("break;");
    }

    internal static void AppendEndpointValidate(
        SharedIndentedBuilder code,
        string target,
        string path,
        string error,
        string kind,
        string errorsVar,
        string badFlag
    )
    {
        code.AppendLine("bool " + badFlag + " = false;");
        // The non-throwing endpoint invariant matches the throwing Validate
        // without constructing an exception per malformed value-free endpoint.
        code.AppendLine("var " + badFlag + "Msg = " + target + ".GetInvariantError();");
        code.AppendLine(
            "if ("
                + badFlag
                + "Msg is not null) { "
                + errorsVar
                + ".Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = "
                + badFlag
                + "Msg }); "
                + badFlag
                + " = true; }"
        );
    }
}
