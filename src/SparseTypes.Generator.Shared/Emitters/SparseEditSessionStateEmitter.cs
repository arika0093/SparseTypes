using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Path-first semantic state surface for generated edit sessions.</summary>
/// <remarks>Emits live <c>FieldState</c> and <c>CollectionState</c> members on each model session.</remarks>
internal static class SparseEditSessionStateEmitter
{
    internal static bool HasStateSurface(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.StateContracts is not null
        && dialect.TransitionContracts is not null
        && dialect.TypedPathInterfaceType is { Length: > 0 };

    internal static void AppendStateMembers(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasStateSurface(dialect))
        {
            return;
        }

        var states = dialect.StateContracts!;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        AppendFieldState(code, modelType, pathType, states, dialect);
        AppendStateSnapshot(code, states);
        AppendCollectionState(code, modelType, members, pathType, states);
        AppendObserveMembers(code, modelType, pathType, states, dialect);
    }

    private static void AppendStateSnapshot(SharedIndentedBuilder code, SparseStateContracts states)
    {
        if (!states.HasQueryView)
        {
            return;
        }

        // One capture per refresh generation: the view retains the change set
        // so F field queries share one root snapshot/diff and one projection.
        code.AppendLine(
            "/// <summary>Captures one reusable state-query snapshot over the pending changes.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Snapshot: derives one change set from the retained baseline and retains it for repeated point-in-time lookups. Live <c>FieldState</c> queries stay unnotified-mutation accurate; share the snapshot only for an explicitly captured generation.</remarks>"
        );
        code.AppendLine(
            "/// <returns>A view serving repeated field queries without re-deriving the diff.</returns>"
        );
        code.AppendLine(
            "public "
                + states.QueryViewType
                + " CaptureStateSnapshot() => new "
                + states.QueryViewType
                + "(CreateChangeSet());"
        );
    }

    private static void AppendFieldState(
        SharedIndentedBuilder code,
        string modelType,
        string pathType,
        SparseStateContracts states,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        // The concrete path struct (like ChangeSet.Find) keeps overload
        // resolution unambiguous against the model-agnostic overload.
        var typedPath = SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue");
        code.AppendLine(
            "/// <summary>Queries the semantic field state at a typed path against the current baseline.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call and never retains subscriptions or per-field caches. Snapshot <see cref=\"CreateChangeSet\"/> for repeated point-in-time lookups.</remarks>"
        );
        code.AppendLine("/// <param name=\"path\">The typed path to inspect.</param>");
        code.AppendLine("/// <typeparam name=\"TValue\">The addressed value type.</typeparam>");
        code.AppendLine(
            "/// <returns>The live field state with presence-aware endpoints.</returns>"
        );
        code.AppendLine(
            "public "
                + states.FieldStateType
                + "<TValue> FieldState<TValue>("
                + typedPath
                + " path)"
        );
        code.OpenBlock();
        if (!dialect.PathIsValueType)
        {
            code.AppendLine(
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLine(
            "return "
                + states.StateQueryType
                + ".FieldState<"
                + modelType
                + ", TValue>(_session.CreateChangeSet(), path);"
        );
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Queries the semantic field state at a model-agnostic path against the current baseline.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Covers paths without a compile-time typed form, such as nested-model builders and missing or deleted members. Unknown paths report a clean state. See the typed overload for the exact/ancestor/descendant contract.</remarks>"
        );
        code.AppendLine("/// <param name=\"path\">The path to inspect.</param>");
        code.AppendLine("/// <returns>The live field state with boxed endpoints.</returns>");
        code.AppendLine(
            "public " + states.FieldStateType + "<object?> FieldState(" + pathType + " path)"
        );
        code.OpenBlock();
        if (!dialect.PathIsValueType)
        {
            code.AppendLine(
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLine(
            "return " + states.StateQueryType + ".FieldState(_session.CreateChangeSet(), path);"
        );
        code.CloseBlock();
    }

    private static void AppendObserveMembers(
        SharedIndentedBuilder code,
        string modelType,
        string pathType,
        SparseStateContracts states,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!states.HasObservedField)
        {
            return;
        }

        // The concrete path struct keeps overload resolution unambiguous
        // against the model-agnostic overload, mirroring FieldState.
        var typedPath = SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue");
        // With a bound query view, one session-owned coordinator shares a
        // single captured snapshot across every watcher in a dispatch
        // generation; without it each watcher keeps its live query.
        var coordinated = states.HasQueryView;
        AppendObserveOverload(
            code,
            modelType,
            states,
            typedPath,
            "TValue",
            true,
            dialect,
            coordinated
        );
        AppendObserveOverload(
            code,
            modelType,
            states,
            pathType,
            "object?",
            false,
            dialect,
            coordinated
        );
    }

    private static void AppendObserveOverload(
        SharedIndentedBuilder code,
        string modelType,
        SparseStateContracts states,
        string pathParameterType,
        string valueType,
        bool typed,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool coordinated
    )
    {
        var kind = typed ? "a typed path" : "a model-agnostic path";
        var typeParameters = typed ? "<TValue>" : string.Empty;
        var watcherType = states.ObservedFieldType + (typed ? "<TValue>" : "<object?>");

        code.AppendLine(
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Opt-in live subscription: refreshes on every session state notification (observable edits, batches, in-place apply, revert, both baseline accepts, reload, reconcile) and raises <see cref=\"global::System.ComponentModel.INotifyPropertyChanged.PropertyChanged\"/> only on effective changes, synchronously with no UI thread marshaling. <c>IsChanged</c> is the semantic baseline diff, not a visited flag. Dispose the watcher to unsubscribe. Key segments keep identity across moves; index segments stay positional."
                + (
                    coordinated
                        ? " One invalidation captures a single state snapshot shared by every watcher in that dispatch; explicit <c>Refresh</c> calls always re-read the live model."
                        : string.Empty
                )
                + "</remarks>"
        );
        code.AppendLine("/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLine("/// <typeparam name=\"TValue\">The addressed value type.</typeparam>");
        }

        code.AppendLine("/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLine("public " + watcherType + " ObserveField" + typeParameters);
        AppendObserveBody(
            code,
            modelType,
            pathParameterType,
            states,
            valueType,
            typed,
            dialect,
            true,
            coordinated
        );
        code.AppendLine(
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Alias for <c>ObserveField</c>; both spellings return the same watcher.</remarks>"
        );
        code.AppendLine("/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLine("/// <typeparam name=\"TValue\">The addressed value type.</typeparam>");
        }

        code.AppendLine("/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLine("public " + watcherType + " WatchField" + typeParameters);
        AppendObserveBody(
            code,
            modelType,
            pathParameterType,
            states,
            valueType,
            typed,
            dialect,
            false,
            coordinated
        );
    }

    private static void AppendObserveBody(
        SharedIndentedBuilder code,
        string modelType,
        string pathParameterType,
        SparseStateContracts states,
        string valueType,
        bool typed,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool primary,
        bool coordinated
    )
    {
        code.AppendLine("(" + pathParameterType + " path)");
        code.OpenBlock();
        if (!dialect.PathIsValueType)
        {
            code.AppendLine(
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        if (primary)
        {
            if (coordinated)
            {
                // The dispatch query reads the generation snapshot while one
                // is bound and falls back to the live model otherwise; the
                // live query serves construction and explicit refreshes.
                var snapshotQuery = typed
                    ? "view.FieldState<" + modelType + ", " + valueType + ">(path)"
                    : "view.FieldState(path)";
                code.AppendLine(
                    "var hooks = new "
                        + states.ObservedFieldHooksType
                        + "<"
                        + valueType
                        + "> { Query = () => FieldState(path), DispatchQuery = () => { var view = __observationView; return view is null ? FieldState(path) : "
                        + snapshotQuery
                        + "; }, Subscribe = handler => __ObservationSubscribe(handler), Unsubscribe = handler => __ObservationUnsubscribe(handler) };"
                );
            }
            else
            {
                code.AppendLine(
                    "var hooks = new "
                        + states.ObservedFieldHooksType
                        + "<"
                        + valueType
                        + "> { Query = () => FieldState(path), Subscribe = handler => PropertyChanged += handler, Unsubscribe = handler => PropertyChanged -= handler };"
                );
            }

            code.AppendLine(
                "return new " + states.ObservedFieldType + "<" + valueType + ">(path, hooks);"
            );
        }
        else
        {
            var typeArguments = typed ? "<TValue>" : string.Empty;
            code.AppendLine("return ObserveField" + typeArguments + "(path);");
        }

        code.CloseBlock();
    }

    // Session-owned observation fan-out: the first watcher subscribes once to
    // the session invalidation signal, each dispatch captures one reusable
    // snapshot, and the last removal releases everything. Reentrant edits
    // during delivery schedule a fresh generation instead of recursing.
    // Private state trails every public session member to keep the
    // public-first generated member ordering; session emitters call this in
    // their private tail.
    internal static void AppendObservationCoordinatorMembers(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (dialect.StateContracts is not { HasObservedField: true, HasQueryView: true } states)
        {
            return;
        }

        AppendObservationCoordinator(code, states);
    }

    private static void AppendObservationCoordinator(
        SharedIndentedBuilder code,
        SparseStateContracts states
    )
    {
        var handlerList =
            "global::System.Collections.Generic.List<global::System.ComponentModel.PropertyChangedEventHandler>";
        var handlerArray = "global::System.ComponentModel.PropertyChangedEventHandler[]";
        code.AppendLine("private " + handlerList + "? __observationHandlers;");
        // Lazily built snapshot of the subscriber list: stable membership
        // reuses one array across dispatch generations instead of copying
        // per generation, while subscribe/dispose during delivery stays safe
        // by traversing the local array.
        code.AppendLine("private " + handlerArray + "? __observationHandlerSnapshot;");
        code.AppendLine("private " + states.QueryViewType + "? __observationView;");
        code.AppendLine("private bool __observationDispatching;");
        code.AppendLine("private bool __observationPending;");
        code.AppendLine(
            "private void __ObservationSubscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)"
        );
        code.OpenBlock();
        code.AppendLine("__observationHandlers ??= new " + handlerList + "();");
        code.AppendLine("if (__observationHandlers.Count == 0)");
        code.OpenBlock();
        code.AppendLine("PropertyChanged += __ObservationInvalidated;");
        code.CloseBlock();
        code.AppendLine("__observationHandlers.Add(handler);");
        code.AppendLine("__observationHandlerSnapshot = null;");
        code.CloseBlock();
        code.AppendLine(
            "private void __ObservationUnsubscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)"
        );
        code.OpenBlock();
        code.AppendLine("if (__observationHandlers is null)");
        code.OpenBlock();
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine("if (__observationHandlers.Remove(handler))");
        code.OpenBlock();
        code.AppendLine("__observationHandlerSnapshot = null;");
        code.AppendLine("if (__observationHandlers.Count == 0)");
        code.OpenBlock();
        code.AppendLine("PropertyChanged -= __ObservationInvalidated;");
        code.AppendLine("__observationHandlers = null;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "private void __ObservationInvalidated(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)"
        );
        code.OpenBlock();
        code.AppendLine("if (__observationDispatching)");
        code.OpenBlock();
        code.AppendLine("__observationPending = true;");
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine("__observationDispatching = true;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("do");
        code.OpenBlock();
        code.AppendLine("__observationPending = false;");
        code.AppendLine("var handlers = __observationHandlers;");
        code.AppendLine("if (handlers is null || handlers.Count == 0)");
        code.OpenBlock();
        code.AppendLine("break;");
        code.CloseBlock();
        // Transient keyed states skip one generation without retaining the
        // snapshot; the next notification captures fresh.
        code.AppendLine(states.QueryViewType + " view;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("view = CaptureStateSnapshot();");
        code.CloseBlock();
        code.AppendLine("catch (global::System.InvalidOperationException)");
        code.OpenBlock();
        code.AppendLine("break;");
        code.CloseBlock();
        code.AppendLine("__observationView = view;");
        code.AppendLine("try");
        code.OpenBlock();
        // The cached array is reused across generations with stable
        // membership; membership changes invalidate it, so the local array
        // stays fixed for this delivery while the next generation rebuilds.
        code.AppendLine("var dispatch = __observationHandlerSnapshot ??= handlers.ToArray();");
        code.AppendLine("foreach (var handler in dispatch)");
        code.OpenBlock();
        code.AppendLine("handler(sender, e);");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("finally");
        code.OpenBlock();
        code.AppendLine("__observationView = null;");
        code.CloseBlock();
        code.AppendLine("} while (__observationPending);");
        code.CloseBlock();
        code.AppendLine("finally");
        code.OpenBlock();
        code.AppendLine("__observationDispatching = false;");
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendCollectionState(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string pathType,
        SparseStateContracts states
    )
    {
        code.AppendLine(
            "/// <summary>Queries the keyed or dictionary collection state at a root collection path.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call; the returned view snapshots that change set. State inspection only: the view never mutates the session. Root collection members only; navigate the change set for nested collections and use <see cref=\"FieldState\"/> for other members.</remarks>"
        );
        code.AppendLine(
            "/// <param name=\"collectionPath\">The root collection member path.</param>"
        );
        code.AppendLine("/// <returns>The live collection state view.</returns>");
        code.AppendLine(
            "/// <exception cref=\"global::System.ArgumentException\">The path does not address a root keyed or dictionary collection member.</exception>"
        );
        code.AppendLine(
            "public "
                + states.CollectionStateType
                + " CollectionState("
                + pathType
                + " collectionPath)"
        );
        code.OpenBlock();
        SparseChangeSetNaming.ComputePublicNames(members, out var propNames, out _);
        var emitted = false;
        foreach (var member in members)
        {
            var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
            var isDict = SparseChangeSetBasicsEmitter.IsDict(member);
            if (!isKeyed && !isDict)
            {
                continue;
            }

            // Full-path equality keeps dispatch exact (root type, member, and
            // collection kind all match) without naming path segment internals.
            var navigation =
                modelType + ".SparsePath." + SparseNaming.EscapeIdentifier(member.Property.Name);
            var property = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var factory = isKeyed ? "KeyedState" : "DictionaryState";
            code.AppendLine(
                "if (collectionPath.Equals(("
                    + pathType
                    + ")"
                    + navigation
                    + ")) return "
                    + states.StateQueryType
                    + "."
                    + factory
                    + "(_session.CreateChangeSet()."
                    + property
                    + ", collectionPath);"
            );
            emitted = true;
        }

        if (!emitted)
        {
            code.AppendLine(
                "throw new global::System.ArgumentException(\"This model has no keyed or dictionary collection members. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }
        else
        {
            code.AppendLine(
                "throw new global::System.ArgumentException(\"The path does not address a root keyed or dictionary collection member. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }

        code.CloseBlock();
    }
}
