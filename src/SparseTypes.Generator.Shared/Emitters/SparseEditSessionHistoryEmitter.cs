namespace SparseTypes.Generator.Shared;

/// <summary>History type names bound by the configured dialect.</summary>
/// <remarks>Shared never hard-codes a runtime history, options, hooks, or contract type.</remarks>
internal sealed record HistoryBinding
{
    public HistoryBinding(
        string historyType,
        string historyOptionsType,
        string historyHooksType,
        string historyContractType,
        string? historyCollectionType,
        string? ownedHistoryViewType
    )
    {
        this.HistoryType = historyType;
        this.HistoryOptionsType = historyOptionsType;
        this.HistoryHooksType = historyHooksType;
        this.HistoryContractType = historyContractType;
        this.HistoryCollectionType = historyCollectionType;
        this.OwnedHistoryViewType = ownedHistoryViewType;
    }

    /// <summary>Reusable history type, such as the typed undo/redo core.</summary>
    public string HistoryType { get; init; }

    /// <summary>History retention options type.</summary>
    public string HistoryOptionsType { get; init; }

    /// <summary>Typed change-set hooks type wiring the concrete change set.</summary>
    public string HistoryHooksType { get; init; }

    /// <summary>Non-generic history control contract, or empty for the legacy factory only.</summary>
    public string HistoryContractType { get; init; }

    public string? HistoryCollectionType { get; init; }

    public string? OwnedHistoryViewType { get; init; }

    /// <summary>Whether the single-owned history surface is bound.</summary>
    public bool HasOwnedHistory => HistoryContractType.Length != 0;
}

/// <summary>Opt-in history factory and session-owned history for edit sessions.</summary>
/// <remarks>Split from the session emitter to keep each file under the size guideline.</remarks>
internal static class SparseEditSessionHistoryEmitter
{
    /// <summary>Binds the configured dialect history surface, if complete.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="binding">Reusable history names when bound.</param>
    /// <returns>True when the history factory must be emitted.</returns>
    internal static bool TryGetHistoryBinding(
        SparseGeneratorConfig config,
        out HistoryBinding? binding
    )
    {
        binding = null;
        var dialect = config.EditSessionDialect;
        if (dialect is null || !dialect.HasHistory)
        {
            return false;
        }

        binding = new HistoryBinding(
            dialect.HistoryType!,
            dialect.HistoryOptionsType!,
            dialect.HistoryHooksType!,
            dialect.HistoryContractType ?? string.Empty,
            dialect.HistoryCollectionType,
            dialect.OwnedHistoryViewType
        );
        return true;
    }

    /// <summary>Appends baseline lifecycle members with history invalidation.</summary>
    /// <remarks>Without a history binding the legacy expression-bodied members are emitted unchanged.</remarks>
    internal static void AppendLifecycleMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string rebaseResultType,
        HistoryBinding? history,
        string? reconcileConflictExpression,
        bool acceptChildrenFirst
    )
    {
        if (history is null)
        {
            AppendLegacyLifecycleMembers(
                code,
                modelType,
                conflictType,
                rebaseResultType,
                reconcileConflictExpression,
                acceptChildrenFirst
            );
            return;
        }

        code.AppendLine("/// <summary>Reverts pending changes to the retained baseline.</summary>");
        code.AppendLine("/// <remarks>Clears registered edit histories on success.</remarks>");
        code.AppendLine("public void RevertChanges()");
        code.OpenBlock();
        code.AppendLine("_session.RevertChanges();");
        code.AppendLine("__SparseClearHistories();");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Reverts the current model to its retained baseline when possible in place.</summary>"
        );
        code.AppendLine(
            "/// <param name=\"conflicts\">Structured conflicts when the pending changes cannot be reverted in place.</param>"
        );
        code.AppendLine(
            "/// <returns><see langword=\"true\"/> when the model was reverted; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLine(
            "/// <remarks>Clears registered edit histories only when the revert succeeds.</remarks>"
        );
        code.AppendLine(
            "public bool TryRevertChanges(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        code.AppendLine("var applied = _session.TryRevertChanges(out conflicts);");
        code.AppendLine("if (applied) __SparseClearHistories();");
        code.AppendLine("return applied;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Reloads the session around the given server state, preserving local edits.</summary>"
        );
        code.AppendLine("/// <param name=\"serverState\">The authoritative server state.</param>");
        code.AppendLine("/// <returns>The rebase outcome with the rebased change set.</returns>");
        code.AppendLine(
            "/// <remarks>Clears registered edit histories only when the reload applies without conflicts.</remarks>"
        );
        code.AppendLine("public " + rebaseResultType + " Reload(" + modelType + " serverState)");
        code.OpenBlock();
        code.AppendLine("var outcome = _session.Reload(serverState);");
        code.AppendLine("if (!outcome.HasConflicts) __SparseClearHistories();");
        code.AppendLine("return outcome;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Accepts the pending changes and advances the baseline.</summary>"
        );
        code.AppendLine("/// <remarks>Clears registered edit histories on success.</remarks>");
        code.AppendLine("public void AcceptChanges()");
        code.OpenBlock();
        code.AppendLine("_session.AcceptChanges();");
        code.AppendLine("__SparseClearHistories();");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Accepts the given change set and advances the baseline.</summary>"
        );
        code.AppendLine("/// <param name=\"changes\">The change set to accept.</param>");
        if (acceptChildrenFirst)
        {
            code.AppendLine(
                "/// <remarks>Merges pending child sessions before advancing the baseline by exactly <paramref name=\"changes\"/>; edits made after the change set was captured stay pending. Clears registered edit histories on success.</remarks>"
            );
            code.AppendLine(
                "/// <exception cref=\"global::System.InvalidOperationException\">Pending child sessions conflict or their target is missing.</exception>"
            );
            code.AppendLine("public void AcceptChanges(" + modelType + ".ChangeSet changes)");
            code.OpenBlock();
            code.AppendLine(
                "if (!TryAcceptChildSessions(out var conflicts)) throw new global::System.InvalidOperationException(conflicts is null ? \"A child edit session target is no longer available.\" : \"A child edit session conflicts with changes in its parent session.\");"
            );
            code.AppendLine("_session.AcceptChanges(changes);");
            code.AppendLine("__SparseClearHistories();");
            code.CloseBlock();
        }
        else
        {
            code.AppendLine("/// <remarks>Clears registered edit histories on success.</remarks>");
            code.AppendLine("public void AcceptChanges(" + modelType + ".ChangeSet changes)");
            code.OpenBlock();
            code.AppendLine("_session.AcceptChanges(changes);");
            code.AppendLine("__SparseClearHistories();");
            code.CloseBlock();
        }
        code.AppendLine(
            "/// <summary>Reconciles a submitted change set with the authoritative persisted model.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Submitted unassigned elements correlate with assigned persisted elements through their shared temporary identities; the persisted model becomes the new baseline with post-submit local edits preserved. Mismatches fail without mutating the session. Clears registered edit histories only when reconciliation succeeds."
                + (
                    acceptChildrenFirst
                        ? " Merges pending child sessions first; edits made after submission stay pending as local edits."
                        : string.Empty
                )
                + "</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"submitted\">The submitted transition to acknowledge.</param>"
        );
        code.AppendLine("/// <param name=\"persisted\">The authoritative persisted model.</param>");
        code.AppendLine(
            "/// <param name=\"error\">The failure reason, or null on success.</param>"
        );
        code.AppendLine(
            "/// <returns><see langword=\"true\"/> on success; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLine(
            "public bool TryReconcile("
                + modelType
                + ".ChangeSet submitted, "
                + modelType
                + " persisted, out string? error)"
        );
        code.OpenBlock();
        if (acceptChildrenFirst)
        {
            code.AppendLine(
                "if (!TryAcceptChildSessions(out var conflicts)) { error = conflicts is null ? \"A child edit session target is no longer available.\" : \"A child edit session conflicts with changes in its parent session.\"; return false; }"
            );
        }

        code.AppendLine(
            "var reconciled = _session.TryReconcile(submitted, persisted, static (s, p) => "
                + modelType
                + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                + modelType
                + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), out error);"
        );
        code.AppendLine("if (reconciled) __SparseClearHistories();");
        code.AppendLine("return reconciled;");
        code.CloseBlock();
        AppendTryReconcileAndApply(
            code,
            modelType,
            conflictType,
            reconcileConflictExpression,
            clearHistories: true,
            acceptChildrenFirst
        );
        AppendHistoryFactory(code, modelType, conflictType, history);
        if (history.HasOwnedHistory)
        {
            AppendOwnedHistoryMembers(code, modelType, conflictType, history);
        }
    }

    /// <summary>Appends the legacy opt-in history factory for one model session.</summary>
    /// <remarks>Advanced multi-observer escape hatch: prefer the single-owned <c>EnableHistory</c> surface for ordinary undo/redo.</remarks>
    private static void AppendHistoryFactory(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        code.AppendLine(
            "/// <summary>Creates an independent opt-in bounded edit history with undo and redo.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Advanced use only; prefer <see cref=\"EnableHistory\"/> for the single session-owned history. Subscribes to effective transition notifications and stores one typed change set per user action. Undo applies the inverted change set instead of storing two copies. A new ordinary edit clears redo. Replayed operations are never re-recorded. <c>BatchEdit</c> publishes one transition and stores one entry. Without this call the session retains no history. The underlying snapshot and diff work runs whether or not a history exists. The caller owns the returned history and must dispose it.</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"options\">Retention options, or null for defaults.</param>"
        );
        code.AppendLine("/// <returns>A disposable history bound to this session.</returns>");
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine(
            "public "
                + historyType
                + " CreateHistory("
                + history.HistoryOptionsType
                + "? options = null)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var hooks = " + HistoryHooksExpression(modelType, conflictType, history) + ";"
        );
        code.AppendLine("var history = new " + historyType + "(options, hooks);");
        if (history.HistoryCollectionType is { Length: > 0 } collectionType)
        {
            code.AppendLine(
                "__histories ??= new " + collectionType + "<" + modelType + ".ChangeSet>();"
            );
            code.AppendLine("__histories.Add(history);");
        }
        else
        {
            code.AppendLine("if (__histories is null)");
            code.AppendLine(
                "__histories = new global::System.Collections.Generic.List<" + historyType + ">();"
            );
            code.AppendLine("__histories.Add(history);");
            code.AppendLine(
                "history.Disposed += () => { if (__histories is not null) __histories.Remove(history); };"
            );
        }
        code.AppendLine("return history;");
        code.CloseBlock();
    }

    /// <summary>Appends the single-owned history surface for one model session.</summary>
    /// <remarks>The session retains one enabled history behind a non-disposable framework-neutral contract.</remarks>
    private static void AppendOwnedHistoryMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        var ownedViewType = history.OwnedHistoryViewType is { Length: > 0 } ownedType
            ? ownedType + "<" + modelType + ".ChangeSet>"
            : "__OwnedHistoryView";
        code.AppendLine(
            "/// <summary>Gets the session-owned edit history, or null when history is not enabled.</summary>"
        );
        code.AppendLine(
            "/// <remarks>No history subscription or retained entries exist until <see cref=\"EnableHistory\"/> runs. Forked and new sessions start without history.</remarks>"
        );
        code.AppendLine(
            "public " + history.HistoryContractType + "? History => __ownedHistoryView;"
        );
        code.AppendLine(
            "/// <summary>Occurs when session-owned history availability changes.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Raised after <see cref=\"EnableHistory\"/> creates the history and after <see cref=\"DisableHistory\"/> releases it. Undo/redo stack changes raise <c>StateChanged</c> on the history itself.</remarks>"
        );
        code.AppendLine("public event global::System.Action? HistoryChanged;");
        code.AppendLine(
            "/// <summary>Enables the single session-owned edit history with undo and redo.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Subscribes to effective transition notifications and stores one typed change set per user action. Repeating the call with the same options returns the existing history without resetting retained edits. Repeating with different options throws; call <see cref=\"DisableHistory\"/> first to reconfigure. The returned control is owned by the session and cannot be disposed by consumers. Without this call the session retains no history.</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"options\">Retention options, or null for defaults.</param>"
        );
        code.AppendLine("/// <returns>The session-owned history control.</returns>");
        code.AppendLine(
            "public "
                + history.HistoryContractType
                + " EnableHistory("
                + history.HistoryOptionsType
                + "? options = null)"
        );
        code.OpenBlock();
        code.AppendLine("if (__ownedHistoryView is not null)");
        code.OpenBlock();
        code.AppendLine(
            "var requestedMaxEntries = (options ?? new "
                + history.HistoryOptionsType
                + "()).MaxEntries;"
        );
        code.AppendLine("if (__ownedHistoryView.MaxEntries != requestedMaxEntries)");
        code.OpenBlock();
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"The session already has an enabled history with different options. Call DisableHistory() first to reconfigure instead of silently resetting retained edits.\");"
        );
        code.CloseBlock();
        code.AppendLine("return __ownedHistoryView;");
        code.CloseBlock();
        code.AppendLine(
            "var hooks = " + HistoryHooksExpression(modelType, conflictType, history) + ";"
        );
        code.AppendLine("var history = new " + historyType + "(options, hooks);");
        code.AppendLine("var view = new " + ownedViewType + "(history);");
        code.AppendLine("__ownedHistoryView = view;");
        code.AppendLine("var historyChanged = HistoryChanged;");
        code.AppendLine("if (historyChanged is not null)");
        code.OpenBlock();
        code.AppendLine("historyChanged();");
        code.CloseBlock();
        code.AppendLine("return view;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Disables the session-owned edit history and releases retention.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Unsubscribes from transitions and drops retained entries. Histories created by the legacy factory are unaffected. No-op when history is not enabled.</remarks>"
        );
        code.AppendLine("public void DisableHistory()");
        code.OpenBlock();
        code.AppendLine("var view = __ownedHistoryView;");
        code.AppendLine("if (view is null)");
        code.OpenBlock();
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine("__ownedHistoryView = null;");
        code.AppendLine(
            history.OwnedHistoryViewType is { Length: > 0 }
                ? "view.DisposeOwned();"
                : "view.__SparseDispose();"
        );
        code.AppendLine("var historyChanged = HistoryChanged;");
        code.AppendLine("if (historyChanged is not null)");
        code.OpenBlock();
        code.AppendLine("historyChanged();");
        code.CloseBlock();
        code.CloseBlock();
    }

    /// <summary>Builds the typed hooks expression wiring one change set.</summary>
    private static string HistoryHooksExpression(
        string modelType,
        string conflictType,
        HistoryBinding history
    ) =>
        "new "
        + history.HistoryHooksType
        + "<"
        + modelType
        + ".ChangeSet> { IsEmpty = static changes => changes.IsEmpty, Invert = static changes => changes.Invert(), Compose = static (first, second) => first.Compose(second), TryApply = ("
        + modelType
        + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? conflicts) => TryApplyInPlace(changes, out conflicts), Subscribe = handler => TransitionObserved += handler, Unsubscribe = handler => TransitionObserved -= handler }";

    /// <summary>Appends history retention fields for one model session.</summary>
    internal static void AppendHistoryFields(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding? history
    )
    {
        if (history is null)
        {
            return;
        }

        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        if (history.HistoryCollectionType is { Length: > 0 } collectionType)
        {
            code.AppendLine(
                "private " + collectionType + "<" + modelType + ".ChangeSet>? __histories;"
            );
        }
        else
        {
            code.AppendLine(
                "private global::System.Collections.Generic.List<" + historyType + ">? __histories;"
            );
        }
        if (history.HasOwnedHistory)
        {
            var ownedViewType = history.OwnedHistoryViewType is { Length: > 0 } ownedType
                ? ownedType + "<" + modelType + ".ChangeSet>"
                : "__OwnedHistoryView";
            code.AppendLine("private " + ownedViewType + "? __ownedHistoryView;");
            if (history.OwnedHistoryViewType is null or { Length: 0 })
            {
                AppendOwnedHistoryView(code, modelType, conflictType, history);
            }
        }

        code.AppendLine("private void __SparseClearHistories()");
        code.OpenBlock();
        if (history.HistoryCollectionType is { Length: > 0 })
        {
            code.AppendLine("__histories?.Clear();");
        }
        else
        {
            code.AppendLine("var histories = __histories;");
            code.AppendLine("if (histories is not null)");
            code.OpenBlock();
            code.AppendLine("for (var index = 0; index < histories.Count; index++)");
            code.AppendLine("histories[index].Clear();");
            code.CloseBlock();
        }
        if (history.HasOwnedHistory)
        {
            code.AppendLine("__ownedHistoryView?.Clear();");
        }

        code.CloseBlock();
    }

    /// <summary>Appends the non-disposable session-owned history view.</summary>
    /// <remarks>The view forwards the control contract to the retained typed history and releases it only through the owning session.</remarks>
    private static void AppendOwnedHistoryView(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        var conflicts =
            "out global::System.Collections.Generic.IReadOnlyList<" + conflictType + ">? conflicts";
        code.AppendLine("/// <summary>Session-owned non-disposable history control.</summary>");
        code.AppendLine("private sealed class __OwnedHistoryView : " + history.HistoryContractType);
        code.OpenBlock();
        code.AppendLine("public __OwnedHistoryView(" + historyType + " history)");
        code.OpenBlock();
        code.AppendLine("_inner = history;");
        code.AppendLine("_inner.StateChanged += OnStateChanged;");
        code.CloseBlock();
        code.AppendLine("public event global::System.Action? StateChanged;");
        code.AppendLine("public bool CanUndo => Inner.CanUndo;");
        code.AppendLine("public bool CanRedo => Inner.CanRedo;");
        code.AppendLine("public int UndoCount => Inner.UndoCount;");
        code.AppendLine("public int RedoCount => Inner.RedoCount;");
        code.AppendLine("public int MaxEntries => Inner.MaxEntries;");
        code.AppendLine("public void Undo() => Inner.Undo();");
        code.AppendLine("public bool TryUndo(" + conflicts + ") => Inner.TryUndo(out conflicts);");
        code.AppendLine("public void Redo() => Inner.Redo();");
        code.AppendLine("public bool TryRedo(" + conflicts + ") => Inner.TryRedo(out conflicts);");
        code.AppendLine("public global::System.IDisposable BeginGroup() => Inner.BeginGroup();");
        code.AppendLine("public void Group(global::System.Action action) => Inner.Group(action);");
        code.AppendLine("public void Clear() => Inner.Clear();");
        code.AppendLine("internal void __SparseDispose()");
        code.OpenBlock();
        code.AppendLine("var history = _inner;");
        code.AppendLine("_inner = null;");
        code.AppendLine("if (history is not null)");
        code.OpenBlock();
        code.AppendLine("history.StateChanged -= OnStateChanged;");
        code.AppendLine("history.Dispose();");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "private "
                + historyType
                + " Inner => _inner ?? throw new global::System.ObjectDisposedException(GetType().FullName);"
        );
        code.AppendLine("private void OnStateChanged()");
        code.OpenBlock();
        code.AppendLine("var raised = StateChanged;");
        code.AppendLine("if (raised is not null)");
        code.OpenBlock();
        code.AppendLine("raised();");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("private " + historyType + "? _inner;");
        code.CloseBlock();
    }

    private static void AppendLegacyLifecycleMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string rebaseResultType,
        string? reconcileConflictExpression,
        bool acceptChildrenFirst
    )
    {
        code.AppendLine("/// <summary>Reverts pending changes to the retained baseline.</summary>");
        code.AppendLine("public void RevertChanges() => _session.RevertChanges();");
        code.AppendLine(
            "/// <summary>Reverts the current model to its retained baseline when possible in place.</summary>"
        );
        code.AppendLine(
            "/// <param name=\"conflicts\">Structured conflicts when the pending changes cannot be reverted in place.</param>"
        );
        code.AppendLine(
            "/// <returns><see langword=\"true\"/> when the model was reverted; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLine(
            "public bool TryRevertChanges(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) => _session.TryRevertChanges(out conflicts);"
        );
        code.AppendLine(
            "/// <summary>Reloads the session around the given server state, preserving local edits.</summary>"
        );
        code.AppendLine("/// <param name=\"serverState\">The authoritative server state.</param>");
        code.AppendLine("/// <returns>The rebase outcome with the rebased change set.</returns>");
        code.AppendLine(
            "public "
                + rebaseResultType
                + " Reload("
                + modelType
                + " serverState) => _session.Reload(serverState);"
        );
        code.AppendLine(
            "/// <summary>Accepts the pending changes and advances the baseline.</summary>"
        );
        code.AppendLine("public void AcceptChanges() => _session.AcceptChanges();");
        code.AppendLine(
            "/// <summary>Accepts the given change set and advances the baseline.</summary>"
        );
        code.AppendLine("/// <param name=\"changes\">The change set to accept.</param>");
        if (acceptChildrenFirst)
        {
            code.AppendLine(
                "/// <remarks>Merges pending child sessions before advancing the baseline by exactly <paramref name=\"changes\"/>; edits made after the change set was captured stay pending.</remarks>"
            );
            code.AppendLine(
                "/// <exception cref=\"global::System.InvalidOperationException\">Pending child sessions conflict or their target is missing.</exception>"
            );
            code.AppendLine("public void AcceptChanges(" + modelType + ".ChangeSet changes)");
            code.OpenBlock();
            code.AppendLine(
                "if (!TryAcceptChildSessions(out var conflicts)) throw new global::System.InvalidOperationException(conflicts is null ? \"A child edit session target is no longer available.\" : \"A child edit session conflicts with changes in its parent session.\");"
            );
            code.AppendLine("_session.AcceptChanges(changes);");
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "public void AcceptChanges("
                    + modelType
                    + ".ChangeSet changes) => _session.AcceptChanges(changes);"
            );
        }
        code.AppendLine(
            "/// <summary>Reconciles a submitted change set with the authoritative persisted model.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Submitted unassigned elements correlate with assigned persisted elements through their shared temporary identities; the persisted model becomes the new baseline with post-submit local edits preserved. Mismatches fail without mutating the session."
                + (
                    acceptChildrenFirst
                        ? " Merges pending child sessions first; edits made after submission stay pending as local edits."
                        : string.Empty
                )
                + "</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"submitted\">The submitted transition to acknowledge.</param>"
        );
        code.AppendLine("/// <param name=\"persisted\">The authoritative persisted model.</param>");
        code.AppendLine(
            "/// <param name=\"error\">The failure reason, or null on success.</param>"
        );
        code.AppendLine(
            "/// <returns><see langword=\"true\"/> on success; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLine(
            "public bool TryReconcile("
                + modelType
                + ".ChangeSet submitted, "
                + modelType
                + " persisted, out string? error)"
        );
        if (acceptChildrenFirst)
        {
            code.OpenBlock();
            code.AppendLine(
                "if (!TryAcceptChildSessions(out var conflicts)) { error = conflicts is null ? \"A child edit session target is no longer available.\" : \"A child edit session conflicts with changes in its parent session.\"; return false; }"
            );
            code.AppendLine(
                "return _session.TryReconcile(submitted, persisted, static (s, p) => "
                    + modelType
                    + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                    + modelType
                    + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), out error);"
            );
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "=> _session.TryReconcile(submitted, persisted, static (s, p) => "
                    + modelType
                    + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                    + modelType
                    + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), out error);"
            );
        }

        AppendTryReconcileAndApply(
            code,
            modelType,
            conflictType,
            reconcileConflictExpression,
            clearHistories: false,
            acceptChildrenFirst
        );
    }

    private static void AppendTryReconcileAndApply(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string? reconcileConflictExpression,
        bool clearHistories,
        bool acceptChildrenFirst
    )
    {
        if (reconcileConflictExpression is null)
        {
            return;
        }

        code.AppendLine(
            "/// <summary>Reconciles an acknowledged submission with persisted values and applies them to the live model.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Temporary identities correlate persisted keys. Persisted values are adopted unless changed locally after submission. Post-submit edits remain pending against the persisted baseline; overlapping remote and local edits return typed-path conflicts. Planning conflicts leave the live model and retained baseline unchanged."
                + (
                    acceptChildrenFirst
                        ? " Merges pending child sessions first; edits made after submission stay pending as local edits."
                        : string.Empty
                )
                + "</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"submitted\">The change set sent to persistence.</param>"
        );
        code.AppendLine("/// <param name=\"persisted\">The authoritative persisted model.</param>");
        code.AppendLine(
            "/// <param name=\"conflicts\">The typed-path reconciliation conflict on failure.</param>"
        );
        code.AppendLine(
            "/// <returns>True when persisted values were adopted; otherwise false.</returns>"
        );
        code.AppendLine(
            "public bool TryReconcileAndApply("
                + modelType
                + ".ChangeSet submitted, "
                + modelType
                + " persisted, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.OpenBlock();
        if (acceptChildrenFirst)
        {
            code.AppendLine(
                "if (!TryAcceptChildSessions(out var acceptConflicts)) { conflicts = acceptConflicts; return false; }"
            );
        }

        code.AppendLine(
            "var reconciled = _session.TryReconcileAndApply(submitted, persisted, static (s, p) => "
                + modelType
                + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                + modelType
                + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), static error => "
                + reconcileConflictExpression
                + ", out conflicts);"
        );
        code.AppendLine("if (reconciled)");
        code.OpenBlock();
        if (clearHistories)
        {
            code.AppendLine("__SparseClearHistories();");
        }

        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine("return false;");
        code.CloseBlock();
    }
}
