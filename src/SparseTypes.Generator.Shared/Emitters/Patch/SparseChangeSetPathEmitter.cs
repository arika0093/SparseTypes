namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetPathEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null,
        string? modelType = null
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        if (target is not null)
        {
            AppendSummary(code);
            code.AppendLine(
                "public global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths() => "
                    + target.ChangeSetOperationsType
                    + ".EnumerateChangedPaths(this);"
            );
            code = target.ChangeSetOperations;
        }

        AppendSummary(code);
        if (target is null)
        {
            code.AppendLine("public global::System.Collections.Generic.IReadOnlyList<" + pathType + "> EnumerateChangedPaths()");
        }
        else
        {
            code.AppendLine("/// <param name=\"self\">The change set to inspect.</param>");
            code.AppendLine(
                "internal static global::System.Collections.Generic.IReadOnlyList<" + pathType + "> EnumerateChangedPaths(ChangeSet self)"
            );
        }

        code.OpenBlock();
        var receiver = target is null ? string.Empty : "self.";
        code.AppendLine("if (" + receiver + "IsEmpty) return global::System.Array.Empty<" + pathType + ">();");
        if (dialect.PathTraversal is not null)
        {
            AppendTraversal(code, dialect.PathTraversal, pathType, modelType ?? "global::System.Object", target);
        }
        else
        {
            AppendProjectionFallback(code, pathType, target);
        }
        code.CloseBlock();
    }

    private static void AppendSummary(SharedIndentedBuilder code)
    {
        code.AppendLine("/// <summary>Enumerates changed member paths, descending through nested models.</summary>");
        code.AppendLine(
            "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
        );
        code.AppendLine("/// <returns>The changed member paths.</returns>");
    }

    private static void AppendTraversal(
        SharedIndentedBuilder code,
        SparsePathTraversalContracts traversal,
        string pathType,
        string modelType,
        SparseOperationTarget? target
    )
    {
        var paths = "__sparse_changed_paths";
        var pathTexts = "__sparse_path_texts";
        var scratch = "__sparse_path_scratch";
        var rootModel = "typeof(" + modelType + ")";
        var self = target is null ? string.Empty : "self.";
        code.AppendLine("var " + paths + " = new global::System.Collections.Generic.List<" + pathType + ">();");
        code.AppendLine("var " + pathTexts + " = new global::System.Collections.Generic.Dictionary<" + pathType + ", string>();");
        code.AppendLine("if (" + self + "__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine("var rootPath = __SparseRootPath;");
        code.AppendLine(pathTexts + ".Add(rootPath, rootPath.ToString());");
        code.AppendLine(paths + ".Add(rootPath);");
        code.AppendLine("return " + paths + ".AsReadOnly();");
        code.CloseBlock();
        code.AppendLine("var " + scratch + " = new " + SparseChangeSetTraversalEmitter.ScratchListType(traversal) + "();");
        code.AppendLine(
            self
                + "__SparseCollectInto(null, __sparse_segments => { var path = "
                + traversal.PathFromSegments(rootModel, "__sparse_segments")
                + "; if (!"
                + pathTexts
                + ".ContainsKey(path)) { "
                + pathTexts
                + ".Add(path, path.ToString()); "
                + paths
                + ".Add(path); } return true; }, "
                + scratch
                + ");"
        );
        code.AppendLine("if (" + paths + ".Count > 1)");
        code.OpenBlock();
        code.AppendLine(
            paths + ".Sort((left, right) => global::System.StringComparer.Ordinal.Compare(" + pathTexts + "[left], " + pathTexts + "[right]));"
        );
        code.CloseBlock();
        code.AppendLine("return " + paths + ".AsReadOnly();");
    }

    private static void AppendProjectionFallback(SharedIndentedBuilder code, string pathType, SparseOperationTarget? target)
    {
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        var paths = "__sparse_changed_paths";
        var pathTexts = "__sparse_path_texts";
        code.AppendLine("var " + paths + " = new global::System.Collections.Generic.List<" + pathType + ">();");
        code.AppendLine("var " + pathTexts + " = new global::System.Collections.Generic.Dictionary<" + pathType + ", string>();");
        code.AppendLine("foreach (var change in " + enumerate + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (!"
                + pathTexts
                + ".ContainsKey(change.Path)) { "
                + pathTexts
                + ".Add(change.Path, change.Path.ToString()); "
                + paths
                + ".Add(change.Path); }"
        );
        code.CloseBlock();
        code.AppendLine("if (" + paths + ".Count > 1)");
        code.OpenBlock();
        code.AppendLine(
            paths + ".Sort((left, right) => global::System.StringComparer.Ordinal.Compare(" + pathTexts + "[left], " + pathTexts + "[right]));"
        );
        code.CloseBlock();
        code.AppendLine("return " + paths + ".AsReadOnly();");
    }
}
