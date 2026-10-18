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
        code.AppendLineAt(
            2,
            "/// <summary>Captures one reusable state-query snapshot over the pending changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Snapshot: derives one change set from the retained baseline and retains it for repeated point-in-time lookups. Live <c>FieldState</c> queries stay unnotified-mutation accurate; share the snapshot only for an explicitly captured generation.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>A view serving repeated field queries without re-deriving the diff.</returns>"
        );
        code.AppendLineAt(
            2,
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
        code.AppendLineAt(
            2,
            "/// <summary>Queries the semantic field state at a typed path against the current baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call and never retains subscriptions or per-field caches. Snapshot <see cref=\"CreateChangeSet\"/> for repeated point-in-time lookups.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The typed path to inspect.</param>");
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The live field state with presence-aware endpoints.</returns>"
        );
        code.AppendLineAt(
            2,
            "public "
                + states.FieldStateType
                + "<TValue> FieldState<TValue>("
                + typedPath
                + " path)"
        );
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLineAt(
            3,
            "return "
                + states.StateQueryType
                + ".FieldState<"
                + modelType
                + ", TValue>(_session.CreateChangeSet(), path);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Queries the semantic field state at a model-agnostic path against the current baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Covers paths without a compile-time typed form, such as nested-model builders and missing or deleted members. Unknown paths report a clean state. See the typed overload for the exact/ancestor/descendant contract.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to inspect.</param>");
        code.AppendLineAt(2, "/// <returns>The live field state with boxed endpoints.</returns>");
        code.AppendLineAt(
            2,
            "public " + states.FieldStateType + "<object?> FieldState(" + pathType + " path)"
        );
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLineAt(
            3,
            "return " + states.StateQueryType + ".FieldState(_session.CreateChangeSet(), path);"
        );
        code.AppendLineAt(2, "}");
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

        code.AppendLineAt(
            2,
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Opt-in live subscription: refreshes on every session state notification (observable edits, batches, in-place apply, revert, both baseline accepts, reload, reconcile) and raises <see cref=\"global::System.ComponentModel.INotifyPropertyChanged.PropertyChanged\"/> only on effective changes, synchronously with no UI thread marshaling. <c>IsChanged</c> is the semantic baseline diff, not a visited flag. Dispose the watcher to unsubscribe. Key segments keep identity across moves; index segments stay positional."
                + (
                    coordinated
                        ? " One invalidation captures a single state snapshot shared by every watcher in that dispatch; explicit <c>Refresh</c> calls always re-read the live model."
                        : string.Empty
                )
                + "</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLineAt(
                2,
                "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
            );
        }

        code.AppendLineAt(2, "/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLineAt(2, "public " + watcherType + " ObserveField" + typeParameters);
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
        code.AppendLineAt(
            2,
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Alias for <c>ObserveField</c>; both spellings return the same watcher.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLineAt(
                2,
                "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
            );
        }

        code.AppendLineAt(2, "/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLineAt(2, "public " + watcherType + " WatchField" + typeParameters);
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
        code.AppendLineAt(2, "(" + pathParameterType + " path)");
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
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
                code.AppendLineAt(
                    3,
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
                code.AppendLineAt(
                    3,
                    "var hooks = new "
                        + states.ObservedFieldHooksType
                        + "<"
                        + valueType
                        + "> { Query = () => FieldState(path), Subscribe = handler => PropertyChanged += handler, Unsubscribe = handler => PropertyChanged -= handler };"
                );
            }

            code.AppendLineAt(
                3,
                "return new " + states.ObservedFieldType + "<" + valueType + ">(path, hooks);"
            );
        }
        else
        {
            var typeArguments = typed ? "<TValue>" : string.Empty;
            code.AppendLineAt(3, "return ObserveField" + typeArguments + "(path);");
        }

        code.AppendLineAt(2, "}");
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
        code.AppendLineAt(2, "private " + handlerList + "? __observationHandlers;");
        code.AppendLineAt(2, "private " + states.QueryViewType + "? __observationView;");
        code.AppendLineAt(2, "private bool __observationDispatching;");
        code.AppendLineAt(2, "private bool __observationPending;");
        code.AppendLineAt(
            2,
            "private void __ObservationSubscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "__observationHandlers ??= new " + handlerList + "();");
        code.AppendLineAt(3, "if (__observationHandlers.Count == 0)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "PropertyChanged += __ObservationInvalidated;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "__observationHandlers.Add(handler);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private void __ObservationUnsubscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__observationHandlers is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "__observationHandlers.Remove(handler);");
        code.AppendLineAt(3, "if (__observationHandlers.Count == 0)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "PropertyChanged -= __ObservationInvalidated;");
        code.AppendLineAt(4, "__observationHandlers = null;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private void __ObservationInvalidated(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__observationDispatching)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__observationPending = true;");
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "__observationDispatching = true;");
        code.AppendLineAt(3, "try");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "do");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__observationPending = false;");
        code.AppendLineAt(5, "var handlers = __observationHandlers;");
        code.AppendLineAt(5, "if (handlers is null || handlers.Count == 0)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "break;");
        code.AppendLineAt(5, "}");
        // Transient keyed states skip one generation without retaining the
        // snapshot; the next notification captures fresh.
        code.AppendLineAt(5, states.QueryViewType + " view;");
        code.AppendLineAt(5, "try");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "view = CaptureStateSnapshot();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "catch (global::System.InvalidOperationException)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "break;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "__observationView = view;");
        code.AppendLineAt(5, "try");
        code.AppendLineAt(5, "{");
        // A fixed copy keeps subscribe/dispose during delivery safe; a
        // watcher added mid-dispatch is already live and joins the next
        // generation.
        code.AppendLineAt(6, "foreach (var handler in handlers.ToArray())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "handler(sender, e);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "finally");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__observationView = null;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "} while (__observationPending);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "finally");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__observationDispatching = false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendCollectionState(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string pathType,
        SparseStateContracts states
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Queries the keyed or dictionary collection state at a root collection path.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call; the returned view snapshots that change set. State inspection only: the view never mutates the session. Root collection members only; navigate the change set for nested collections and use <see cref=\"FieldState\"/> for other members.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"collectionPath\">The root collection member path.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The live collection state view.</returns>");
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentException\">The path does not address a root keyed or dictionary collection member.</exception>"
        );
        code.AppendLineAt(
            2,
            "public "
                + states.CollectionStateType
                + " CollectionState("
                + pathType
                + " collectionPath)"
        );
        code.AppendLineAt(2, "{");
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
            code.AppendLineAt(
                3,
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
            code.AppendLineAt(
                3,
                "throw new global::System.ArgumentException(\"This model has no keyed or dictionary collection members. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "throw new global::System.ArgumentException(\"The path does not address a root keyed or dictionary collection member. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }

        code.AppendLineAt(2, "}");
    }
}
