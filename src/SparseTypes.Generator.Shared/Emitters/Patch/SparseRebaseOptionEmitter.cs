namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-rebase options plumbing shared by ChangeSet and Patch rebase.</summary>
/// <remarks>Generated rebase entry points accept a caller-owned options object carrying redacted-before paths.</remarks>
internal static class SparseRebaseOptionEmitter
{
    internal static string OptionsType(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        SparseFragmentPatchEmitter.GetRebaseOptionsType(dialect);

    internal static string ModeType(SparseFragmentPatchEmitter.SparsePatchDialect dialect) => SparseFragmentPatchEmitter.GetRebaseModeType(dialect);

    internal static string RedactedKind(SparseFragmentPatchEmitter.SparsePatchDialect dialect) => dialect.ConflictKindType + ".RedactedBefore";

    internal const string RedactedReason = "The change carries a redacted before-state and strict rebase rejects it.";

    internal static void AppendHelpers(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect)
    {
        var options = OptionsType(dialect);
        code.AppendLine("/// <summary>Whether the given dotted path is redacted-before under the supplied options.</summary>");
        code.AppendLine(
            "private static bool __SparseIsRedacted(" + options + "? options, string path) => options is not null && options.IsRedactedBefore(path);"
        );
        code.AppendLine("/// <summary>Whether strict rebase rejects redacted-before members.</summary>");
        code.AppendLine(
            "private static bool __SparseRejectsRedacted("
                + options
                + "? options) => options is not null && options.RejectChangesWithRedactedBeforeValuesDuringRebase;"
        );
    }

    internal static void AppendRedactedConflict(
        SharedIndentedBuilder code,
        string runtime,
        string conflict,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string pathExpression,
        string conflictsVariable
    )
    {
        var missing = runtime + "Optional<object?>.Missing";
        code.AppendLine(
            conflictsVariable
                + ".Add(new "
                + conflict
                + "("
                + pathExpression
                + ", "
                + RedactedKind(dialect)
                + ", "
                + missing
                + ", "
                + missing
                + ", "
                + missing
                + ", \""
                + RedactedReason
                + "\"));"
        );
    }
}
