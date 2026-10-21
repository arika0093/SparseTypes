namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionChangeSetEmitter
{
    internal static void AppendMembers(
        SharedIndentedBuilder code,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        bool hasChildModelMembers = true
    )
    {
        code.AppendLine(
            "/// <summary>Runs observable edits as one transition notification, including nested batches.</summary>"
        );
        code.AppendLine("/// <param name=\"edit\">The observable edits to run.</param>");
        code.AppendLine(
            "public void BatchEdit(global::System.Action edit) => _session.BatchEdit(edit);"
        );

        if (
            SparseEditSessionEmitter.HasChildSessionSurface(
                config,
                patchDialect,
                hasChildModelMembers
            )
        )
        {
            AppendChildSessionChangeSetMembers(code, modelType, patchDialect);
        }
        else
        {
            code.AppendLine(
                "/// <summary>Creates a change set describing the pending changes.</summary>"
            );
            code.AppendLine(
                "/// <remarks>Capturing never mutates the session; accepting stays explicit.</remarks>"
            );
            code.AppendLine(
                "/// <returns>The pending change set; empty when there are no changes.</returns>"
            );
            code.AppendLine(
                "public "
                    + modelType
                    + ".ChangeSet CreateChangeSet() => _session.CreateChangeSet();"
            );
        }

        code.AppendLine(
            "/// <summary>Creates a read-only snapshot of own changes owned by this session.</summary>"
        );
        code.AppendLine(
            "/// <remarks>This does not accept or otherwise mutate registered child sessions, and it excludes their pending edits. Use <see cref=\"CreateChangeSet\"/> for the child-aware submission preview.</remarks>"
        );
        code.AppendLine("/// <returns>This session's own pending change set.</returns>");
        code.AppendLine(
            "public " + modelType + ".ChangeSet PreviewChangeSet() => _session.CreateChangeSet();"
        );

        if (config.EditSessionWithChangeSetInterfaceMetadataName is { Length: > 0 } adapterName)
        {
            var adapterType =
                "global::" + adapterName + "<" + modelType + ", " + modelType + ".ChangeSet>";
            code.AppendLine(
                "bool "
                    + adapterType
                    + ".IsEmpty("
                    + modelType
                    + ".ChangeSet changes) => changes.IsEmpty;"
            );
            code.AppendLine(
                "global::System.Collections.Generic.IReadOnlyList<"
                    + SparseFragmentPatchEmitter.GetPathType(patchDialect)
                    + "> "
                    + adapterType
                    + ".EnumerateChangedPaths("
                    + modelType
                    + ".ChangeSet changes) => changes.EnumerateChangedPaths();"
            );
        }
    }

    private static void AppendChildSessionChangeSetMembers(
        SharedIndentedBuilder code,
        string modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        code.AppendLine(
            "/// <summary>Creates a child-aware preview of the sendable change set without accepting child sessions.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Projects pending child and descendant edits onto a detached candidate and composes them with the session's own changes. Never accepts, discards, or detaches child drafts; merge them explicitly with <see cref=\"TryAcceptChildSessions\"/>. A missing target reports failure with null conflicts and preserves drafts.</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"changes\">The previewed change set on success; otherwise null.</param>"
        );
        code.AppendLine(
            "/// <param name=\"conflicts\">Child-session conflicts on failure.</param>"
        );
        code.AppendLine(
            "/// <returns>True when the preview was prepared; otherwise false.</returns>"
        );
        code.AppendLine(
            "public bool TryCreateChangeSet(out "
                + modelType
                + ".ChangeSet? changes, out global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (!TryBuildChildAcceptanceCandidate(out var candidate, out var hasPendingChildChanges, out conflicts)) { changes = null; return false; }"
        );
        code.AppendLine(
            "if (!hasPendingChildChanges || candidate is null) { changes = PreviewChangeSet(); conflicts = null; return true; }"
        );
        code.AppendLine(
            "var childChanges = "
                + modelType
                + ".ChangeSet.Between(_session.GetModelForFrameworkAccess(), candidate);"
        );
        code.AppendLine(
            "changes = childChanges.IsEmpty ? PreviewChangeSet() : PreviewChangeSet().Compose(childChanges);"
        );
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Creates a child-aware preview of the sendable change set without accepting child sessions.</summary>"
        );
        code.AppendLine(
            "/// <remarks>See <see cref=\"TryCreateChangeSet\"/> for the preview contract.</remarks>"
        );
        code.AppendLine("/// <returns>The previewed change set.</returns>");
        code.AppendLine(
            "/// <exception cref=\"global::System.InvalidOperationException\">A child session conflicts with this session, or its target is missing.</exception>"
        );
        code.AppendLine(
            "public "
                + modelType
                + ".ChangeSet CreateChangeSet() => TryCreateChangeSet(out var changes, out var conflicts) ? changes! : throw new global::System.InvalidOperationException(conflicts is null ? \"A child edit session target is no longer available.\" : \"A child edit session conflicts with changes in its parent session.\");"
        );
    }
}
