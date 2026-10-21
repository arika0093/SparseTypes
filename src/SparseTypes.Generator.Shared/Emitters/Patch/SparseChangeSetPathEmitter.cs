namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetPathEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        if (target is not null)
        {
            code.AppendLine(
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLine(
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLine("/// <returns>The changed member paths.</returns>");
            code.AppendLine(
                "public global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths() => "
                    + target.ChangeSetOperationsType
                    + ".EnumerateChangedPaths(this);"
            );
            code = target.ChangeSetOperations;
            code.AppendLine(
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLine(
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLine("/// <param name=\"self\">The change set to enumerate.</param>");
            code.AppendLine("/// <returns>The changed member paths.</returns>");
            code.AppendLine(
                "internal static global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths(ChangeSet self)"
            );
        }
        else
        {
            code.AppendLine(
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLine(
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLine("/// <returns>The changed member paths.</returns>");
            code.AppendLine(
                "public global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths()"
            );
        }
        code.OpenBlock();
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        code.AppendLine(target is null ? "if (IsEmpty)" : "if (self.IsEmpty)");
        code.OpenBlock();
        code.AppendLine("return global::System.Array.Empty<" + pathType + ">();");
        code.CloseBlock();
        code.AppendLine(
            "var paths = new global::System.Collections.Generic.List<" + pathType + ">();"
        );
        code.AppendLine(
            "var seen = new global::System.Collections.Generic.HashSet<" + pathType + ">();"
        );
        code.AppendLine("foreach (var change in " + enumerate + ")");
        code.OpenBlock();
        code.AppendLine("if (seen.Add(change.Path)) paths.Add(change.Path);");
        code.CloseBlock();
        code.AppendLine("if (paths.Count > 1)");
        code.OpenBlock();
        code.AppendLine(
            "paths.Sort(static (left, right) => global::System.StringComparer.Ordinal.Compare(left.ToString(), right.ToString()));"
        );
        code.CloseBlock();
        code.AppendLine("return paths.AsReadOnly();");
        code.CloseBlock();
    }
}
