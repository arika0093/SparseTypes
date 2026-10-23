namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionChildEmitter
{
    internal static void AppendPendingStateProjection(SharedIndentedBuilder code, string modelType, string conflictType, string interfaceMetadataName)
    {
        var pendingStateInterface = "global::" + interfaceMetadataName + "<" + modelType + ", " + conflictType + ">";
        code.AppendLine(
            "bool "
                + pendingStateInterface
                + ".TryApplyPendingState("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine("if (current is null) throw new global::System.ArgumentNullException(nameof(current));");
        code.AppendLine("var candidate = current;");
        code.AppendLine("var ownChanges = PreviewChangeSet();");
        code.AppendLine(
            "if (!ownChanges.IsEmpty && !ownChanges.TryApplyTo(current, out candidate, out conflicts)) { updated = null; return false; }"
        );
        code.AppendLine("updated = candidate;");
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
    }

    internal static string ChildRegistrationTuple(string pathType, string modelType, string conflictType) =>
        "("
        + pathType
        + " Path, global::System.Func<bool> HasChanges, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryPreflight, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryAccept, global::System.Func<"
        + modelType
        + ", (bool Accepted, "
        + modelType
        + "? Updated, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryApply, global::System.Action Discard, global::System.Action Confirm, global::System.Func<bool> IsTargetMissing)";

    internal static void AppendChildOverlapGuard(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect patchDialect)
    {
        if (patchDialect.ChildPathOverlapExpression is not { } childPathOverlap)
        {
            return;
        }

        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("for (var leftIndex = 0; leftIndex < __sparse_child_sessions.Count; leftIndex++)");
        code.OpenBlock();
        code.AppendLine("var left = __sparse_child_sessions[leftIndex]; if (!left.HasChanges()) continue;");
        code.AppendLine("for (var rightIndex = leftIndex + 1; rightIndex < __sparse_child_sessions.Count; rightIndex++)");
        code.OpenBlock();
        code.AppendLine("var right = __sparse_child_sessions[rightIndex]; if (!right.HasChanges()) continue;");
        code.AppendLine(
            "if ("
                + childPathOverlap("left.Path", "right.Path")
                + ") { candidate = null; hasPendingChildChanges = false; conflicts = null; return false; }"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendCoordinator(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditSessionWithChildrenInterfaceMetadataName is not { Length: > 0 }
        )
        {
            return;
        }

        code.AppendLine("/// <summary>Gets whether a child session or descendant has pending changes.</summary>");
        code.AppendLine("public bool HasPendingChildSessions");
        code.OpenBlock();
        code.AppendLine("get");
        code.OpenBlock();
        code.AppendLine("if (__sparse_child_sessions is null) return false;");
        code.AppendLine("foreach (var child in __sparse_child_sessions) if (child.HasChanges()) return true;");
        code.AppendLine("return false;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("/// <summary>Applies this session and its pending child sessions to a separate model candidate.</summary>");
        code.AppendLine("/// <param name=\"current\">The model state to rebase onto.</param>");
        code.AppendLine("/// <param name=\"updated\">The candidate model on success.</param>");
        code.AppendLine("/// <param name=\"conflicts\">Structured conflicts when application fails.</param>");
        code.AppendLine("/// <returns>True when all pending changes were applied; otherwise false.</returns>");
        code.AppendLine(
            "public bool TryApplyPendingState("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine("if (current is null) throw new global::System.ArgumentNullException(nameof(current));");
        code.AppendLine("var candidate = current;");
        code.AppendLine("var ownChanges = PreviewChangeSet();");
        code.AppendLine(
            "if (!ownChanges.IsEmpty && !ownChanges.TryApplyTo(current, out candidate, out conflicts)) { updated = null; return false; }"
        );
        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var child in __sparse_child_sessions)");
        code.OpenBlock();
        code.AppendLine("if (!child.HasChanges()) continue;");
        code.AppendLine("var result = child.TryApply(candidate);");
        code.AppendLine("if (!result.Accepted) { updated = null; conflicts = result.Conflicts; return false; }");
        code.AppendLine("candidate = result.Updated!;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("updated = candidate;");
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        var statusType = config.EditChildSessionStatusType!;
        code.AppendLine("/// <summary>Gets the status of a registered child session for a typed path.</summary>");
        code.AppendLine("/// <param name=\"path\">The generated path to the child model.</param>");
        code.AppendLine("/// <returns>The status of a child session registered for the path.</returns>");
        code.AppendLine(
            "public "
                + statusType
                + " GetChildSessionStatus<TChild, TChildPaths, TChildSession>(global::"
                + config.EditChildPathInterfaceMetadataName
                + "<"
                + modelType
                + ", TChild, TChildPaths, TChildSession> path)"
        );
        code.AppendLine(
            "where TChild : class where TChildPaths : "
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TChild> where TChildSession : global::"
                + config.EditSessionWithPathsInterfaceMetadataName
                + "<TChild, TChildPaths>"
        );
        code.OpenBlock();
        code.AppendLine("if (path is null) throw new global::System.ArgumentNullException(nameof(path));");
        code.AppendLine("var found = false;");
        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var child in __sparse_child_sessions)");
        code.OpenBlock();
        code.AppendLine("if (!child.Path.Equals(path.UntypedPath)) continue;");
        code.AppendLine("found = true;");
        code.AppendLine("if (child.IsTargetMissing()) return " + statusType + ".TargetMissing;");
        code.AppendLine("if (child.HasChanges()) return " + statusType + ".OpenChanged;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return found ? " + statusType + ".OpenUnchanged : " + statusType + ".NotOpen;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Checks pending child and descendant merges without changing session state.</summary>");
        code.AppendLine(
            "/// <remarks>Success and failure both leave values, baselines, histories, and registrations unchanged. A missing target reports failure with null conflicts and preserves the draft.</remarks>"
        );
        code.AppendLine("/// <param name=\"conflicts\">Structured conflicts when a child merge cannot be prepared.</param>");
        code.AppendLine("/// <returns>True when all pending merges are conflict-free; otherwise false.</returns>");
        code.AppendLine(
            "public bool TryPreflightChildSessions(out global::System.Collections.Generic.IReadOnlyList<" + conflictType + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine("return TryBuildChildAcceptanceCandidate(out _, out _, out conflicts);");
        code.CloseBlock();
        code.AppendLine("/// <summary>Attempts to merge every pending child session into this session.</summary>");
        code.AppendLine(
            "/// <remarks>Validates every descendant onto one detached candidate, commits it with a single write, then acknowledges children parent-first. Typed conflicts and missing targets fail without mutating parent, child, or descendant state; arbitrary setter or notification exceptions propagate after a best-effort model rollback with no child acknowledged. A rolled-back attempt may still appear in the parent history, and edits made from commit notifications observe undefined freshness.</remarks>"
        );
        code.AppendLine("/// <param name=\"conflicts\">Conflicts when a child could not be merged.</param>");
        code.AppendLine("/// <returns>True when all pending children were merged; otherwise false.</returns>");
        code.AppendLine("public bool TryAcceptChildSessions(out global::System.Collections.Generic.IReadOnlyList<" + conflictType + ">? conflicts)");
        code.OpenBlock();
        code.AppendLine("if (!TryBuildChildAcceptanceCandidate(out var candidate, out var hasPendingChildChanges, out conflicts)) return false;");
        code.AppendLine("if (!hasPendingChildChanges || candidate is null) { conflicts = null; return true; }");
        code.AppendLine("var liveModel = _session.GetModelForFrameworkAccess();");
        code.AppendLine("var snapshotModel = " + modelType + ".Fragment.From(liveModel).ToModel();");
        code.AppendLine("var aggregateChanges = " + modelType + ".ChangeSet.Between(liveModel, candidate);");
        code.AppendLine("if (aggregateChanges.IsEmpty) { ConfirmChildSessions(); conflicts = null; return true; }");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("if (!_session.TryApplyInPlace(aggregateChanges, out conflicts)) return false;");
        code.CloseBlock();
        code.AppendLine("catch (global::System.Exception commitFailure)");
        code.OpenBlock();
        code.AppendLine("global::System.Exception? restoreFailure = null;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("var restoreChanges = " + modelType + ".ChangeSet.Between(_session.GetModelForFrameworkAccess(), snapshotModel);");
        code.AppendLine(
            "if (!restoreChanges.IsEmpty && !_session.TryApplyInPlace(restoreChanges, out _)) throw new global::System.InvalidOperationException(\"The pre-commit model could not be restored after a failed child session commit.\");"
        );
        code.CloseBlock();
        code.AppendLine("catch (global::System.Exception restoreException)");
        code.OpenBlock();
        code.AppendLine("restoreFailure = restoreException;");
        code.CloseBlock();
        code.AppendLine(
            "if (restoreFailure is not null) throw new global::System.AggregateException(\"The child session commit failed and restoring the pre-commit model failed.\", commitFailure, restoreFailure);"
        );
        code.AppendLine("throw;");
        code.CloseBlock();
        code.AppendLine("ConfirmChildSessions();");
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Acknowledges staged child writes after their parent commit succeeded.</summary>");
        code.AppendLine(
            "/// <remarks>Coordinator use only: prefer <see cref=\"TryAcceptChildSessions\"/>. Runs parent-first so descendant confirmations observe committed state. Throws when a target cannot be confirmed; already-confirmed children stay confirmed, so callers may retry after restoring the target.</remarks>"
        );
        code.AppendLine("public void ConfirmChildSessions()");
        code.OpenBlock();
        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var child in __sparse_child_sessions)");
        code.AppendLine("if (child.HasChanges()) child.Confirm();");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("/// <summary>Discards pending edits in every registered child session.</summary>");
        code.AppendLine("public void DiscardChildSessions()");
        code.OpenBlock();
        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var child in __sparse_child_sessions)");
        code.AppendLine("if (child.HasChanges()) child.Discard();");
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendCandidateBuilder(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        code.AppendLine("/// <summary>Projects every pending child and descendant edit onto a detached candidate.</summary>");
        code.AppendLine("/// <remarks>Pure projection shared by preflight, acceptance, and submission previews.</remarks>");
        code.AppendLine("/// <param name=\"candidate\">The candidate model on success; otherwise null.</param>");
        code.AppendLine("/// <param name=\"hasPendingChildChanges\">True when at least one child has pending changes.</param>");
        code.AppendLine("/// <param name=\"conflicts\">Structured conflicts when a child merge cannot be prepared.</param>");
        code.AppendLine("/// <returns>True when all pending merges are conflict-free; otherwise false.</returns>");
        code.AppendLine(
            "private bool TryBuildChildAcceptanceCandidate(out "
                + modelType
                + "? candidate, out bool hasPendingChildChanges, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        AppendChildOverlapGuard(code, patchDialect);
        code.AppendLine("candidate = _session.GetModelForFrameworkAccess();");
        code.AppendLine("hasPendingChildChanges = false;");
        code.AppendLine("if (__sparse_child_sessions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var child in __sparse_child_sessions)");
        code.OpenBlock();
        code.AppendLine("if (!child.HasChanges()) continue;");
        code.AppendLine("hasPendingChildChanges = true;");
        code.AppendLine("var result = child.TryApply(candidate);");
        code.AppendLine("if (!result.Accepted) { candidate = null; hasPendingChildChanges = false; conflicts = result.Conflicts; return false; }");
        code.AppendLine("candidate = result.Updated!;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
    }

    internal static void AppendCoordinatorStorage(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string pathType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditSessionWithChildrenInterfaceMetadataName is not { Length: > 0 }
        )
        {
            return;
        }

        code.AppendLine(
            "private global::System.Collections.Generic.List<"
                + ChildRegistrationTuple(pathType, modelType, conflictType)
                + ">? __sparse_child_sessions;"
        );
    }

    internal static void AppendOpenMethod(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseGeneratorConfig config
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditChildPathInterfaceMetadataName is not { Length: > 0 } pathInterfaceName
            || config.EditChildSessionInterfaceMetadataName is not { Length: > 0 } childSessionInterfaceName
            || config.EditChildSessionImplementationType is not { Length: > 0 } implementationType
        )
        {
            return;
        }

        var pathInterface = "global::" + pathInterfaceName + "<" + modelType + ", TChild, TChildPaths, TChildSession>";
        var childSessionInterface =
            "global::" + childSessionInterfaceName + "<TChild, TChildPaths, " + patchDialect.ConflictType + ", TChildSession" + ">";
        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        code.AppendLine("/// <summary>Opens a path-addressed child model as an independent edit session.</summary>");
        code.AppendLine(
            "/// <remarks>The child has its own typed paths and history. Accepting it merges the child draft into this session and resets the child's history baseline.</remarks>"
        );
        code.AppendLine("/// <param name=\"path\">The generated path to the child model.</param>");
        code.AppendLine("/// <returns>A child edit session connected to this session.</returns>");
        code.AppendLine(
            "public "
                + childSessionInterface
                + " OpenChildSession<TChild, TChildPaths, TChildSession>("
                + pathInterface
                + " path) where TChild : class where TChildPaths : "
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TChild> where TChildSession : global::"
                + config.EditSessionWithPathsInterfaceMetadataName
                + "<TChild, TChildPaths>"
        );
        code.OpenBlock();
        code.AppendLine("if (path is null) throw new global::System.ArgumentNullException(nameof(path));");
        code.AppendLine("var draft = CreateChildDraft(path);");
        code.AppendLine("var baseline = path.Clone(draft.Model);");
        code.AppendLine("var child = path.CreateEditSession(baseline, draft.Model, draft.RefreshParentDraft);");
        code.AppendLine("global::System.Action? detach = null;");
        code.AppendLine(
            "var connected = new "
                + implementationType
                + "<TChild, TChildPaths, "
                + patchDialect.ConflictType
                + ", TChildSession>(child, path.CreateRootPaths(), baseline, path.Clone, _ => { if (!draft.TryAcceptIntoParent(out var conflicts)) return (false, (TChild?)null, conflicts); return (true, draft.Model, null); }, draft.RefreshParentDraft, () => { draft.Dispose(); detach?.Invoke(); }, () => { var accepted = draft.TryPreflightIntoParent(out var conflicts); return (accepted, conflicts); }, () => { draft.ConfirmAccept(); return draft.Model; }, () => draft.IsTargetMissing, () => draft.IsDetached);"
        );
        code.AppendLine(
            "var registration = (Path: path.UntypedPath, HasChanges: (global::System.Func<bool>)(() => connected.HasChanges), TryPreflight: (global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(() => { var accepted = connected.TryPreflightAcceptChanges(out var conflicts); return (accepted, conflicts); }), TryAccept: (global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(() => { var accepted = connected.TryAcceptChanges(out var conflicts); return (accepted, conflicts); }), TryApply: (global::System.Func<"
                + modelType
                + ", (bool Accepted, "
                + modelType
                + "? Updated, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(current => { var candidate = "
                + modelType
                + ".Fragment.From(current).ToModel(); var candidateSession = new "
                + sessionSimple
                + "(candidate, candidate, null); if (candidateSession.Descriptors.ResolveModel(path.UntypedPath) is not TChild target) return (false, ("
                + modelType
                + "?)null, null); if (!connected.TryApplyPendingState(target, out var updatedChild, out var childConflicts)) { if (childConflicts is not null) { var prefixed = new global::System.Collections.Generic.List<"
                + patchDialect.ConflictType
                + ">(childConflicts.Count); foreach (var conflict in childConflicts) prefixed.Add("
                + patchDialect.ChildConflictPathPrefixExpression!("conflict", "path.UntypedPath")
                + "); childConflicts = prefixed; } return (false, ("
                + modelType
                + "?)null, childConflicts); } path.WriteModelValueInPlace(target, updatedChild!); return (true, candidate, null); }), Discard: (global::System.Action)(connected.DiscardChanges), Confirm: (global::System.Action)(() => connected.ConfirmAccept()), IsTargetMissing: (global::System.Func<bool>)(() => connected.IsTargetMissing));"
        );
        code.AppendLine("detach = () => __sparse_child_sessions?.Remove(registration);");
        code.AppendLine(
            "__sparse_child_sessions ??= new global::System.Collections.Generic.List<"
                + ChildRegistrationTuple(pathType, modelType, patchDialect.ConflictType)
                + ">();"
        );
        code.AppendLine("__sparse_child_sessions.Add(registration);");
        code.AppendLine("return connected;");
        code.CloseBlock();
    }

    /// <summary>Appends the fork/merge facade members shared by both session placements.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="core">Qualified reusable core type for this model.</param>
    /// <param name="conflictType">Qualified structured conflict type.</param>
    internal static void AppendForkMergeMembers(SharedIndentedBuilder code, string core, string conflictType, string sessionSimple = "EditSession")
    {
        // Public facade first; the internal core-backed ctor trails with the
        // other internal ctors so generated members stay public-first ordered.
        code.AppendLine("/// <summary>Creates an independent speculative branch of this session.</summary>");
        code.AppendLine(
            "/// <remarks>The fork uses the same session type with a private mutable copy of the current model and a baseline equal to the current state at fork time. Edits on either side stay isolated and the fork starts without the change callback. This is not a transaction: use <see cref=\"TryMergeFrom\"/> to reconcile a fork, <see cref=\"BatchEdit\"/> to group notifications, <see cref=\"AcceptChanges()\"/> to acknowledge a save, and <see cref=\"Reload\"/> to rebase onto server state.</remarks>"
        );
        code.AppendLine("/// <returns>A new session with its own live model, baseline, and bindings.</returns>");
        code.AppendLine("public " + sessionSimple + " Fork() => new " + sessionSimple + "(_session.Fork());");
        code.AppendLine("/// <summary>Attempts to merge a fork created by <see cref=\"Fork\"/> without modifying either session.</summary>");
        code.AppendLine(
            "/// <remarks>The fork transition is rebased onto this session's current model. On success the returned session retains this session's original baseline with the merged model as its current state. A conflict leaves both sessions unchanged. Merging an unrelated session or the session itself throws; both live models are read without locking.</remarks>"
        );
        code.AppendLine("/// <param name=\"fork\">A session created by <see cref=\"Fork\"/> in the same lineage.</param>");
        code.AppendLine("/// <param name=\"merged\">The new session on success; otherwise null.</param>");
        code.AppendLine("/// <param name=\"conflicts\">Structured rebase conflicts on failure; otherwise null.</param>");
        code.AppendLine("/// <returns><see langword=\"true\"/> when the fork merged cleanly; otherwise <see langword=\"false\"/>.</returns>");
        code.AppendLine(
            "public bool TryMergeFrom("
                + sessionSimple
                + " fork, out "
                + sessionSimple
                + "? merged, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine("if (fork is null) throw new global::System.ArgumentNullException(nameof(fork));");
        code.AppendLine(
            "if (_session.TryMergeFrom(fork._session, out var mergedCore, out conflicts) && mergedCore is not null) { merged = new "
                + sessionSimple
                + "(mergedCore); return true; }"
        );
        code.AppendLine("merged = null;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    /// <summary>Appends the core-backed constructor used by fork merges.</summary>
    internal static void AppendForkMergeCoreConstructor(SharedIndentedBuilder code, string core, string sessionSimple) =>
        code.AppendLine("private " + sessionSimple + "(" + core + " session) { _session = session; }");

    /// <summary>Appends the public child-session API.</summary>
    internal static void AppendChildSessionApiMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect,
        SparseGeneratorConfig config
    )
    {
        if (descriptorDialect is null || patchDialect.TypedPathInterfaceType is not { Length: > 0 })
        {
            return;
        }

        SparseEditSessionChildEmitter.AppendCoordinator(code, modelType, patchDialect.ConflictType, config, patchDialect);
        SparseEditSessionChildEmitter.AppendOpenMethod(code, modelType, sessionSimple, patchDialect, config);
    }

    /// <summary>Appends the generated host adapter for the runtime child-draft coordinator.</summary>
    internal static void AppendChildDraftMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect,
        SparseGeneratorConfig config
    )
    {
        if (
            descriptorDialect is null
            || patchDialect.TypedPathInterfaceType is not { Length: > 0 }
            || config.EditChildDraftHostInterfaceMetadataName is not { Length: > 0 } hostName
            || config.EditChildDraftImplementationType is not { Length: > 0 }
        )
        {
            return;
        }

        var conflictType = patchDialect.ConflictType;
        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        var liveChangeSet = patchDialect.StateContracts is { } states
            ? "fork._session." + states.CreateChangeSetFromLiveModelMember + "()"
            : "fork.CreateChangeSet()";

        code.AppendLine(sessionSimple + " global::" + hostName + "<" + sessionSimple + ", " + conflictType + ">.ForkForChildDraft() => Fork();");
        code.AppendLine(
            "bool global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.IsInSameLineage("
                + sessionSimple
                + " other) => other is not null && _session.IsInSameLineage(other._session);"
        );
        code.AppendLine(
            "object? global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.ResolveChildDraftModel("
                + pathType
                + " path) => Descriptors.ResolveModel(path);"
        );
        code.AppendLine(
            "void global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.RefreshAfterChildDraftMutation() => _session.RefreshAfterExternalMutation();"
        );
        code.AppendLine(
            "bool global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.TryAcceptChildDraft<TChild>("
                + sessionSimple
                + " fork, "
                + pathType
                + " path, global::System.Action<TChild, TChild> writeModelValue, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) where TChild : class"
        );
        code.OpenBlock();
        code.AppendLine("var changes = " + liveChangeSet + ";");
        code.AppendLine("if (changes.IsEmpty) { conflicts = null; return true; }");
        code.AppendLine("var rebased = changes.RebaseOnto(_session.GetModelForFrameworkAccess());");
        code.AppendLine("if (rebased.HasConflicts) { conflicts = rebased.Conflicts; return false; }");
        code.AppendLine(
            "if (!rebased.Rebased.IsEmpty) { var candidate = Fork(); if (!candidate.TryApplyInPlace(rebased.Rebased, out conflicts)) return false; if (Descriptors.ResolveModel(path) is not TChild currentTarget || candidate.Descriptors.ResolveModel(path) is not TChild updatedTarget) { conflicts = null; return false; } try { writeModelValue(currentTarget, updatedTarget); } finally { _session.RefreshAfterExternalMutation(); } }"
        );
        code.AppendLine(
            "if (Descriptors.ResolveModel(path) is not TChild latestTarget || fork.Descriptors.ResolveModel(path) is not TChild draftTarget) { conflicts = null; return false; }"
        );
        code.AppendLine("writeModelValue(draftTarget, latestTarget);");
        code.AppendLine("fork.AcceptChanges();");
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine(
            "bool global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.TryPreflightChildDraft<TChild>("
                + sessionSimple
                + " fork, "
                + pathType
                + " path, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) where TChild : class"
        );
        code.OpenBlock();
        code.AppendLine("var changes = " + liveChangeSet + ";");
        code.AppendLine("if (changes.IsEmpty) { conflicts = null; return true; }");
        code.AppendLine("var rebased = changes.RebaseOnto(_session.GetModelForFrameworkAccess());");
        code.AppendLine("if (rebased.HasConflicts) { conflicts = rebased.Conflicts; return false; }");
        code.AppendLine(
            "if (!rebased.Rebased.IsEmpty) { var candidate = Fork(); if (!candidate.TryApplyInPlace(rebased.Rebased, out conflicts)) return false; if (Descriptors.ResolveModel(path) is not TChild || candidate.Descriptors.ResolveModel(path) is not TChild) { conflicts = null; return false; } }"
        );
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine(
            "void global::"
                + hostName
                + "<"
                + sessionSimple
                + ", "
                + conflictType
                + ">.ConfirmChildDraft<TChild>("
                + sessionSimple
                + " fork, "
                + pathType
                + " path, global::System.Action<TChild, TChild> writeModelValue) where TChild : class"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (Descriptors.ResolveModel(path) is not TChild latestTarget || fork.Descriptors.ResolveModel(path) is not TChild draftTarget) throw new global::System.InvalidOperationException(\"The child edit session target is no longer available.\");"
        );
        code.AppendLine("writeModelValue(draftTarget, latestTarget);");
        code.AppendLine("fork.AcceptChanges();");
        code.CloseBlock();
    }

    /// <summary>Appends private storage and the path-specific runtime-draft factory.</summary>
    internal static void AppendChildDraftPrivateMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect,
        SparseGeneratorConfig config
    )
    {
        if (
            descriptorDialect is null
            || patchDialect.TypedPathInterfaceType is not { Length: > 0 }
            || config.EditChildDraftHostInterfaceMetadataName is not { Length: > 0 }
            || config.EditChildDraftImplementationType is not { Length: > 0 } draftType
        )
        {
            return;
        }

        var conflictType = patchDialect.ConflictType;
        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        var typedPath = patchDialect.TypedPathInterfaceType + "<" + modelType + ", TChild>";
        AppendCoordinatorStorage(code, modelType, conflictType, pathType, config, patchDialect);
        code.AppendLine(
            "private "
                + draftType
                + "<TChild, "
                + sessionSimple
                + ", "
                + conflictType
                + "> CreateChildDraft<TChild>("
                + typedPath
                + " path) where TChild : class => "
                + draftType
                + "<TChild, "
                + sessionSimple
                + ", "
                + conflictType
                + ">.Create(this, path);"
        );
    }
}
