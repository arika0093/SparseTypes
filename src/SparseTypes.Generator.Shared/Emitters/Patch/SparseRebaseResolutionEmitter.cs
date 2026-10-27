using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Path-based rebase conflict resolution surface and build core.</summary>
/// <remarks>Emits the configured public rebase result.</remarks>
internal static class SparseRebaseResolutionEmitter
{
    internal static void AppendResolution(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        SparseOperationTarget? target = null,
        SharedIndentedBuilder? infra = null,
        SharedIndentedBuilder? surfaceInfra = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var optionalObject = runtime + "Optional<object?>";
        var rebaseResult = dialect.RebaseResult("ChangeSet");
        var conflict = dialect.ConflictType;
        var choices = "global::System.Collections.Generic.IReadOnlyList<" + runtime + "ResolvedChoice>";
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);

        var shell = code;
        AppendBeginResolutionShell(shell, rebaseResult, optionalFragment, modelType, target);
        AppendResolutionShell(shell, runtime, rebaseResult, optionalFragment, modelType, choices, target);

        // Internal infrastructure trails the public surface.
        var body = target?.ChangeSetOperations ?? infra ?? code;
        AppendBeginResolutionBody(body, rebaseResult, optionalFragment, target);
        SparseResolutionApplyEmitter.AppendForwarderShell(surfaceInfra ?? shell, pathType, conflict, optionalObject, dialect, target);
        AppendTryCreateResolved(
            body,
            members,
            runtime,
            optionalFragment,
            optionalObject,
            rebaseResult,
            conflict,
            choices,
            modelType,
            dialect,
            target
        );
        SparseResolutionApplyEmitter.AppendApplier(
            body,
            members,
            runtime,
            optionalFragment,
            optionalObject,
            conflict,
            pathType,
            modelType,
            dialect,
            target
        );
    }

    internal static void AppendForwarderOnly(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null,
        SharedIndentedBuilder? infra = null,
        SharedIndentedBuilder? surfaceInfra = null
    )
    {
        // Structural hosts and other model-less change sets still participate
        // as recursion targets through the fragment applier.
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        var conflict = dialect.ConflictType;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        SparseResolutionApplyEmitter.AppendForwarderShell(surfaceInfra ?? code, pathType, conflict, optionalObject, dialect, target);
        var body = target?.ChangeSetOperations ?? infra ?? code;
        SparseResolutionApplyEmitter.AppendApplier(
            body,
            members,
            runtime,
            runtime + "Optional<Fragment?>",
            optionalObject,
            conflict,
            pathType,
            null,
            dialect,
            target
        );
    }

    private static void AppendBeginResolutionShell(
        SharedIndentedBuilder shell,
        string rebaseResult,
        string optionalFragment,
        string modelType,
        SparseOperationTarget? target
    )
    {
        shell.AppendLine("/// <summary>Begins path-based resolution of a rebase result against the authoritative current state.</summary>");
        shell.AppendLine(
            "/// <remarks>Pass the same current snapshot used for RebaseOnto. TryBuild fails when the state moved instead of resolving against stale values.</remarks>"
        );
        shell.AppendLine("/// <param name=\"rebase\">The original rebase result to resolve.</param>");
        shell.AppendLine("/// <param name=\"current\">The authoritative current-state snapshot.</param>");
        shell.AppendLine("/// <returns>Mutable resolution state owning the result and the current snapshot.</returns>");
        shell.AppendLine(
            target is null
                ? "public static Resolution BeginResolution("
                    + rebaseResult
                    + " rebase, "
                    + optionalFragment
                    + " current) => new Resolution(rebase, current);"
                : "public static Resolution BeginResolution("
                    + rebaseResult
                    + " rebase, "
                    + optionalFragment
                    + " current) => "
                    + target.ChangeSetOperationsType
                    + ".BeginResolution(rebase, current);"
        );
        shell.AppendLine("/// <summary>Begins path-based resolution of a rebase result against an ordinary model.</summary>");
        shell.AppendLine("/// <param name=\"rebase\">The original rebase result to resolve.</param>");
        shell.AppendLine("/// <param name=\"current\">The authoritative current model.</param>");
        shell.AppendLine("/// <returns>Mutable resolution state owning the result and the current snapshot.</returns>");
        shell.AppendLine(
            "public static Resolution BeginResolution("
                + rebaseResult
                + " rebase, "
                + modelType
                + " current) => BeginResolution(rebase, "
                + optionalFragment
                + ".Present(Fragment.From(current)));"
        );
    }

    private static void AppendResolutionShell(
        SharedIndentedBuilder shell,
        string runtime,
        string rebaseResult,
        string optionalFragment,
        string modelType,
        string choices,
        SparseOperationTarget? target
    )
    {
        shell.AppendLine("/// <summary>Mutable path-based resolution state for '" + modelType + "' rebase conflicts.</summary>");
        shell.AppendLine(
            "/// <remarks>Framework-neutral inspection and resolution without UI state. Decisions are keyed by canonical paths; typed SparsePath overloads are inherited from the base.</remarks>"
        );
        shell.AppendLine("public sealed class Resolution : " + ResolutionBaseFor(runtime));
        shell.OpenBlock();
        shell.AppendLine("/// <summary>Creates resolution state for a rebase result and its current snapshot.</summary>");
        shell.AppendLine("/// <param name=\"rebase\">The original rebase result to resolve.</param>");
        shell.AppendLine("/// <param name=\"current\">The authoritative current-state snapshot.</param>");
        shell.AppendLine("public Resolution(" + rebaseResult + " rebase, " + optionalFragment + " current)");
        shell.Indent().AppendLine(": base(rebase)").Unindent();
        shell.OpenBlock();
        shell.AppendLine("_current = current;");
        shell.CloseBlock();
        shell.AppendLine("/// <summary>Creates resolution state for a rebase result and an ordinary model.</summary>");
        shell.AppendLine("/// <param name=\"rebase\">The original rebase result to resolve.</param>");
        shell.AppendLine("/// <param name=\"current\">The authoritative current model.</param>");
        shell.AppendLine("public Resolution(" + rebaseResult + " rebase, " + modelType + " current)");
        shell.Indent().AppendLine(": this(rebase, " + optionalFragment + ".Present(Fragment.From(current)))").Unindent();
        shell.OpenBlock();
        shell.CloseBlock();
        shell.AppendLine("/// <summary>Gets the authoritative current-state snapshot this resolution builds against.</summary>");
        shell.AppendLine("public " + optionalFragment + " Current => _current;");
        shell.AppendLine("/// <summary>Builds the resolved change set from per-conflict desired values.</summary>");
        shell.AppendLine("/// <param name=\"choices\">Every conflict with the presence-aware value to write.</param>");
        shell.AppendLine("/// <param name=\"resolved\">The resolved change set.</param>");
        shell.AppendLine("/// <param name=\"failureReason\">A value-free reason when the build fails.</param>");
        shell.AppendLine("/// <returns><see langword=\"true\"/> when the change set built.</returns>");
        shell.AppendLine(
            "protected override bool TryCreateResolved("
                + choices
                + " choices, out ChangeSet? resolved, out string? failureReason) => "
                + (target is null ? "__SparseBuildResolved" : target.ChangeSetOperationsType + ".__SparseBuildResolved")
                + "(Rebase, _current, choices, out resolved, out failureReason);"
        );
        shell.AppendLine(
            "/// <summary>The authoritative current-state snapshot this resolution builds against (resolution infrastructure).</summary>"
        );
        shell.AppendLine("private readonly " + optionalFragment + " _current;");
        shell.CloseBlock();
    }

    private static string ResolutionBaseFor(string runtime) => runtime + "SparseRebaseResolution<ChangeSet>";

    private static void AppendBeginResolutionBody(
        SharedIndentedBuilder body,
        string rebaseResult,
        string optionalFragment,
        SparseOperationTarget? target
    )
    {
        if (target is null)
        {
            return;
        }

        body.AppendLine("/// <summary>Begins path-based resolution of a rebase result against the authoritative current state.</summary>");
        body.AppendLine(
            "internal static ChangeSet.Resolution BeginResolution("
                + rebaseResult
                + " rebase, "
                + optionalFragment
                + " current) => new ChangeSet.Resolution(rebase, current);"
        );
    }

    private static void AppendTryCreateResolved(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string optionalObject,
        string rebaseResult,
        string conflict,
        string choices,
        string modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        _ = members;
        _ = runtime;
        _ = optionalObject;
        _ = conflict;
        body.AppendLine("/// <summary>Builds a baseline-aware resolved change set from per-conflict desired values.</summary>");
        body.AppendLine(
            "/// <remarks>Applies the clean rebased patch onto the stored current snapshot, writes each chosen value through the fragment applier, and diffs back against the same snapshot. Never mutates inputs.</remarks>"
        );
        body.AppendLine(
            (target is null ? "private static bool __SparseBuildResolved(" : "internal static bool __SparseBuildResolved(")
                + rebaseResult
                + " rebase, "
                + optionalFragment
                + " current, "
                + choices
                + " choices, out ChangeSet? resolved, out string? failureReason)"
        );
        body.OpenBlock();
        body.AppendLine("resolved = null;");
        body.AppendLine("failureReason = null;");
        body.AppendLine(optionalFragment + " workingRoot = current;");
        body.AppendLine("foreach (var choice in choices)");
        body.OpenBlock();
        body.AppendLine("if (!choice.Conflict.Path.IsRoot) continue;");
        body.AppendLine("if (!__SparseApplyRoot(ref workingRoot, choice.Desired, out failureReason)) return false;");
        body.CloseBlock();
        body.AppendLine("if (!workingRoot.IsPresent || workingRoot.Value is null)");
        body.OpenBlock();
        body.AppendLine("foreach (var choice in choices)");
        body.OpenBlock();
        body.AppendLine("if (!choice.Conflict.Path.IsRoot)");
        body.OpenBlock();
        body.AppendLine("failureReason = \"Only root presence can be resolved while the current state is missing.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.CloseBlock();
        body.AppendLine("resolved = ChangeSet.Between(current, workingRoot, rebase.Rebased.RecordedAt);");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (!rebase.Rebased.__SparseBeforeMatches(current))");
        body.OpenBlock();
        body.AppendLine("failureReason = \"The current state changed since the rebase; rebase onto the latest state before resolving.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var working = current.Value!;");
        body.AppendLine("if (!rebase.Rebased.IsEmpty)");
        body.OpenBlock();
        body.AppendLine("var applied = rebase.Rebased.ToPatch().Apply(current);");
        body.AppendLine("if (!applied.IsPresent || applied.Value is null)");
        body.OpenBlock();
        body.AppendLine("failureReason = \"The rebased change no longer applies to the current state.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("working = applied.Value;");
        body.CloseBlock();
        if (dialect.ResolutionWorkspaceType is not null)
        {
            body.AppendLine("var workspace = new " + dialect.ResolutionWorkspaceType + "();");
        }

        body.AppendLine("foreach (var choice in choices)");
        body.OpenBlock();
        body.AppendLine("if (choice.Conflict.Path.IsRoot) continue;");
        body.AppendLine(
            "if (!__SparseTryApplyResolution(working, choice.Conflict.Path, 0, choice.Desired"
                + SparseResolutionApplyEmitter.WorkspaceArgument(dialect.ResolutionWorkspaceType)
                + ", out var updated, out failureReason)) return false;"
        );
        body.AppendLine("if (updated is null)");
        body.OpenBlock();
        body.AppendLine("failureReason = \"The resolution produced no fragment.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("working = updated;");
        body.CloseBlock();
        body.AppendLine("resolved = ChangeSet.Between(current, " + optionalFragment + ".Present(working), rebase.Rebased.RecordedAt);");
        body.AppendLine("failureReason = null;");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("/// <summary>Applies a root presence resolution (resolution infrastructure).</summary>");
        body.AppendLine(
            (target is null ? "private static bool __SparseApplyRoot(ref " : "internal static bool __SparseApplyRoot(ref ")
                + optionalFragment
                + " workingRoot, "
                + optionalObject
                + " desired, out string? error)"
        );
        body.OpenBlock();
        body.AppendLine("error = null;");
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("workingRoot = " + optionalFragment + ".Missing;");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (desired.Value is null)");
        body.OpenBlock();
        body.AppendLine("workingRoot = " + optionalFragment + ".Present((Fragment?)null);");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (desired.Value is Fragment fragment)");
        body.OpenBlock();
        body.AppendLine("workingRoot = " + optionalFragment + ".Present(fragment.DeepClone());");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("if (desired.Value is " + modelType + " model)");
        body.OpenBlock();
        body.AppendLine("workingRoot = " + optionalFragment + ".Present(Fragment.From(model));");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("error = \"The root resolution value must be a fragment or model.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
    }
}
