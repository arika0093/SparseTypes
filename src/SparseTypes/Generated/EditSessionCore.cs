#pragma warning disable S104 // Mechanical port mirrors the template 1:1; splitting would obscure correspondence.
#pragma warning disable IDE0011 // Verbatim port; compact branches preserved for template correspondence.
// Ported from the product-neutral generator template with the SparseTypes dialect applied.
// The template remains the product-neutral source for downstream generators; this runtime copy
// removes the per-compilation Generated.EditSessionCore sources for SparseTypes consumers.
#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes.Generated;

/// <summary>Tracks edits to a live model against a retained fragment baseline without depending on a UI framework.</summary>
/// <remarks>Factories, session surface, acceptance, construction, and shared state.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public partial class EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
{
    // Public surface first; internal constructors and private state follow.
    // Public surface first; internal constructors and private state follow.
    /// <summary>Creates a session capturing the current model state as its baseline.</summary>
    public static EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, TObservable> toObservable
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);

        return new EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            (value, _) => toObservable(value)
        );
    }

    /// <summary>Creates a session whose observable proxy reports model changes to the session.</summary>
    public static EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateNotificationDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);
        return new EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable,
            onChanged
        );
    }

    /// <summary>Creates a session editing <paramref name="current"/>
    /// against a separate baseline snapshot.</summary>
    public static EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel baseline,
        TModel current,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, TObservable> toObservable
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);

        return new EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            (value, _) => toObservable(value)
        );
    }

    /// <summary>Creates a session with a separate baseline and an observable change callback.</summary>
    public static EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel baseline,
        TModel current,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateNotificationDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);
        return new EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable,
            onChanged
        );
    }

    /// <summary>The live editable model. The UI or application mutates this instance directly.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public TModel Model
    {
        get
        {
            DisableHasChangesCache();
            return _model;
        }
        private set => _model = value;
    }

    /// <summary>Returns the live model for trusted framework use without affecting the change cache.</summary>
    /// <remarks>Unlike <see cref="Model"/>, this access keeps observable-change caching intact.</remarks>
    public TModel GetModelForFrameworkAccess() => _model;

    /// <summary>A stable typed observable proxy over <see cref="Model"/>.</summary>
    public TObservable Observable => _observable;

    /// <summary>The revision of the retained baseline.</summary>
    public long BaselineRevision => _baselineRevision;

    /// <summary>Whether the current model differs semantically from the retained baseline.</summary>
    /// <remarks>Observable edits defer the baseline comparison until this property is read, reusing the latest observed snapshot.</remarks>
    public bool HasChanges
    {
        get
        {
            if (_cacheObservableChanges && _lastObservedSnapshotValid && _hasChangesCacheValid)
            {
                return _cachedHasChanges;
            }

            TChangeSet changes;
            try
            {
                changes = CreateChangeSet();
            }
            catch (InvalidOperationException exception) when (IsDuplicateKeyError(exception))
            {
                return true;
            }

            if (_cacheObservableChanges && _lastObservedSnapshotValid && _hasChangesCacheValid)
            {
                return _cachedHasChanges;
            }

            if (_cacheObservableChanges && _lastObservedSnapshotValid)
            {
                CacheHasChanges(changes);
                return _cachedHasChanges;
            }

            return !_isEmpty(changes);
        }
    }

    /// <summary>Raised when session state or its observable model may have changed.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised with the leaf transitions produced by each observable edit.</summary>
    public event Action<TChangeSet>? TransitionObserved;

    /// <summary>Runs observable edits as one transition notification, including nested batches.</summary>
    /// <remarks>Intermediate snapshot and diff work is deferred.</remarks>
    public void BatchEdit(Action edit)
    {
        if (edit is null)
            throw new ArgumentNullException(nameof(edit));

        var isOuterBatch = _batchDepth == 0;
        if (isOuterBatch)
        {
            _batchStart = _lastObserved;
            _batchDirty = false;
            _batchPropertyChanged = false;
            _batchOnChanged = false;
        }

        _batchDepth++;
        try
        {
            edit();
        }
        finally
        {
            _batchDepth--;
            if (isOuterBatch)
            {
                CompleteBatchEdit();
            }
        }
    }

    private void CompleteBatchEdit()
    {
        var start = _batchStart;
        _batchStart = default;
        var notifyPropertyChanged = _batchPropertyChanged;
        _batchPropertyChanged = false;
        var notifyOnChanged = _batchOnChanged;
        _batchOnChanged = false;
        var dirty = _batchDirty;
        _batchDirty = false;

        if (dirty)
        {
            PublishBatchedTransition(start);
        }
        else if (start.IsPresent)
        {
            // No observable mutation ran inside the batch. Recompute
            // nothing; deferred notifications below still flush.
        }

        if (notifyPropertyChanged)
        {
            OnPropertyChanged(nameof(HasChanges));
        }

        if (notifyOnChanged)
        {
            _onChanged?.Invoke();
        }
    }

    private void PublishBatchedTransition(global::SparseTypes.Optional<TFragment?> start)
    {
        var current = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model));
        if (!start.IsPresent)
        {
            SetObservedSnapshot(current);
            return;
        }

        TChangeSet transition;
        try
        {
            transition = _between(start, current);
        }
        catch (InvalidOperationException exception) when (IsDuplicateKeyError(exception))
        {
            // The net batch state cannot be diffed; keep the last valid
            // observation so later diffs still start from valid state.
            MarkObservedSnapshotInvalid();
            return;
        }

        SetObservedSnapshot(current);

        if (!_isEmpty(transition))
        {
            TransitionObserved?.Invoke(transition);
        }
    }

    /// <summary>Derives the baseline-aware change set between the retained baseline and current model.</summary>
    public TChangeSet CreateChangeSet()
    {
        if (_cacheObservableChanges && _lastObservedSnapshotValid)
        {
            return GetChangeSetForObservedSnapshot();
        }

        return CreateChangeSetFromLiveModel();
    }

    /// <summary>Derives a change set from a fresh capture of the live model.</summary>
    /// <remarks>Unlike <see cref="CreateChangeSet"/>, this method does not reuse the last observed snapshot.</remarks>
    public TChangeSet CreateChangeSetFromLiveModel()
    {
        var current = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model));
        return _between(_baseline, current);
    }

    /// <summary>Derives the baseline-free patch from the current baseline-aware change set.</summary>
    public TPatch CreatePatch() => _toPatch(CreateChangeSet());

    /// <summary>Enumerates changed generated member paths from the current model.</summary>
    public IReadOnlyList<global::SparseTypes.SparsePath> EnumerateChangedPaths()
    {
        if (_enumerateChangedPaths is null)
        {
            throw new NotSupportedException("This edit session does not provide generated change paths.");
        }

        return _enumerateChangedPaths(CreateChangeSet());
    }

    /// <summary>Accepts the current model state as the new baseline.</summary>
    /// <remarks>Rejects sentinels and duplicate keys before retaining the baseline.</remarks>
    public void AcceptChanges()
    {
        // Read the backing field directly.
        var candidate = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model));
        // Fail-fast when the live model cannot serve as a keyed baseline.
        _between(candidate, _baseline);
        SetBaseline(candidate);
        SetObservedSnapshot(candidate);
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Advances the retained baseline by the supplied transition without touching the live model.</summary>
    /// <remarks>The transition's before-state must match the retained baseline on every changed path.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/>
    /// is null.</exception>
    /// <exception cref="InvalidOperationException">The transition is stale or cannot produce a valid baseline.</exception>
    public void AcceptChanges(TChangeSet changes)
    {
        if (changes is null)
            throw new ArgumentNullException(nameof(changes));
        // Validate and project first; only a valid candidate replaces the
        // baseline.
        var advanced = _advanceBaseline(changes, _baseline);
        if (!advanced.IsPresent || advanced.Value is null)
            throw new InvalidOperationException("Advancing the baseline did not produce a valid model state.");
        try
        {
            // Reject candidates that would break future keyed diffs, such
            // as unassigned keyed sentinels.
            _between(advanced, _baseline);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Accepting this change set would retain an invalid baseline, such as unassigned keyed sentinels.",
                ex
            );
        }
        SetBaseline(advanced);
        InvalidateHasChangesCache();
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Initializes a session with observable notifications.</summary>
    /// <param name="model">The live editable model.</param>
    /// <param name="baseline">The retained baseline fragment.</param>
    /// <param name="fromModel">Captures a model as a fragment.</param>
    /// <param name="between">Derives a change set between fragment states.</param>
    /// <param name="toPatch">Projects a change set to a patch.</param>
    /// <param name="isEmpty">Tests whether a change set is empty.</param>
    /// <param name="advanceBaseline">Advances a baseline by a change set.</param>
    /// <param name="toObservable">Creates an observable proxy.</param>
    /// <param name="onChanged">Callback raised after observable edits.</param>
    internal EditSessionCore(
        TModel model,
        TFragment baseline,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
        : this(model, baseline, fromModel, between, toPatch, isEmpty, advanceBaseline, (value, changed, _) => toObservable(value, changed), onChanged)
    { }

    /// <summary>Initializes a session with full in-place support.</summary>
    /// <param name="model">The live editable model.</param>
    /// <param name="baseline">The retained baseline fragment.</param>
    /// <param name="fromModel">Captures a model as a fragment.</param>
    /// <param name="between">Derives a change set between fragment states.</param>
    /// <param name="toPatch">Projects a change set to a patch.</param>
    /// <param name="isEmpty">Tests whether a change set is empty.</param>
    /// <param name="advanceBaseline">Advances a baseline by a change set.</param>
    /// <param name="toObservable">Creates an observable proxy with raw-model access.</param>
    /// <param name="onChanged">Callback raised after observable edits.</param>
    /// <param name="tryApplyTo">Applies a change set to a model.</param>
    /// <param name="writeModel">Writes an updated model into the live instance.</param>
    /// <param name="invert">Inverts a change set.</param>
    /// <param name="rebase">Rebases a change set onto newer state.</param>
    /// <param name="enumerateChangedPaths">Enumerates changed paths.</param>
    /// <param name="refreshObservable">Refreshes observable notifications.</param>
    /// <param name="cacheObservableChanges">Whether to cache observable changes.</param>
    /// <param name="baselineToModel">Materializes a fragment as a model.</param>
    /// <param name="tryApplyInPlace">Applies a prepared change set directly to the live model.</param>
    internal EditSessionCore(
        TModel model,
        TFragment baseline,
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, Action?, TObservable> toObservable,
        Action? onChanged,
        Func<TChangeSet, TModel, (TModel? Updated, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts)>? tryApplyTo = null,
        Action<TModel, TModel>? writeModel = null,
        Func<TChangeSet, TChangeSet>? invert = null,
        Func<TChangeSet, TModel, global::SparseTypes.RebaseResult<TChangeSet>>? rebase = null,
        Func<TChangeSet, IReadOnlyList<global::SparseTypes.SparsePath>>? enumerateChangedPaths = null,
        Action<TObservable>? refreshObservable = null,
        bool cacheObservableChanges = false,
        Func<TFragment, TModel>? baselineToModel = null,
        Func<TChangeSet, TModel, (bool Succeeded, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts)>? tryApplyInPlace = null
    )
    {
        Model = model;
        _fromModel = fromModel;
        _between = between;
        _toPatch = toPatch;
        _isEmpty = isEmpty;
        _advanceBaseline = advanceBaseline;
        _toObservable = toObservable;
        _tryApplyTo = tryApplyTo;
        _writeModel = writeModel;
        _invert = invert;
        _rebase = rebase;
        _enumerateChangedPaths = enumerateChangedPaths;
        _refreshObservable = refreshObservable;
        _onChanged = onChanged;
        _tryApplyInPlace = tryApplyInPlace;
        _cacheObservableChanges = cacheObservableChanges;
        _initialCacheObservableChanges = cacheObservableChanges;
        _baselineToModel = baselineToModel;
        // Fresh sessions start their own branch lineage; Fork keeps the
        // receiver lineage while TryMergeFrom preserves it on the result.
        _rootId = Guid.NewGuid();
        _baseline = global::SparseTypes.Optional<TFragment?>.Present(baseline);
        _lastObserved = global::SparseTypes.Optional<TFragment?>.Present(fromModel(model));
        _observable = toObservable(model, OnObservableMutation, DisableHasChangesCache);
    }

    // Private state and helpers trail the internal surface.
    private const string DuplicateKeyErrorMessage = "Duplicate key in keyed collection.";
    private const string UnassignedBaselineErrorMessage = "An unassigned key cannot appear in the baseline of a keyed collection transition.";
    private const string UnassignedChangeSetBaselineErrorMessage = "An unassigned key cannot appear in the current baseline of a keyed ChangeSet.";
    private readonly Func<TModel, TFragment> _fromModel;
    private readonly Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> _between;
    private readonly Func<TChangeSet, TPatch> _toPatch;
    private readonly Func<TChangeSet, bool> _isEmpty;
    private readonly Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> _advanceBaseline;
    private readonly TObservable _observable;
    private readonly Func<TChangeSet, TModel, (TModel? Updated, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts)>? _tryApplyTo;
    private readonly Func<TChangeSet, TModel, (bool Succeeded, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts)>? _tryApplyInPlace;
    private readonly Action<TModel, TModel>? _writeModel;
    private readonly Func<TChangeSet, TChangeSet>? _invert;
    private readonly Func<TChangeSet, TModel, global::SparseTypes.RebaseResult<TChangeSet>>? _rebase;
    private readonly Func<TChangeSet, IReadOnlyList<global::SparseTypes.SparsePath>>? _enumerateChangedPaths;
    private readonly Func<TFragment, TModel>? _baselineToModel;
    private readonly Func<TModel, Action?, Action?, TObservable> _toObservable;
    private readonly Action<TObservable>? _refreshObservable;
    private readonly Action? _onChanged;
    private readonly bool _initialCacheObservableChanges;
    private Guid _rootId;
    private bool _cacheObservableChanges;
    private bool _hasChangesCacheValid;
    private bool _cachedHasChanges;
    private bool _cachedChangeSetValid;
    private bool _lastObservedSnapshotValid;
    private long _observedGeneration;
    private long _cachedChangeSetGeneration;
    private long _cachedChangeSetBaselineRevision;
    private TChangeSet? _cachedChangeSet;
    private global::SparseTypes.Optional<TFragment?> _lastObserved;
    private global::SparseTypes.Optional<TFragment?> _baseline;
    private long _baselineRevision;

    // Last submission returned as reconciled by TryReconcile, by reference.
    private TChangeSet? _lastReconciledSubmission;
    private global::SparseTypes.Optional<TFragment?> _batchStart;
    private int _batchDepth;
    private bool _batchDirty;
    private bool _batchPropertyChanged;
    private bool _batchOnChanged;

    private TModel _model = null!;

    private void SetBaseline(global::SparseTypes.Optional<TFragment?> baseline)
    {
        var revision = checked(_baselineRevision + 1);
        _baseline = baseline;
        _baselineRevision = revision;
        InvalidateHasChangesCache();
    }

    private void SetObservedSnapshot(global::SparseTypes.Optional<TFragment?> snapshot)
    {
        _lastObserved = snapshot;
        _observedGeneration = unchecked(_observedGeneration + 1);
        _lastObservedSnapshotValid = true;
        InvalidateHasChangesCache();
    }

    private void MarkObservedSnapshotInvalid()
    {
        _observedGeneration = unchecked(_observedGeneration + 1);
        _lastObservedSnapshotValid = false;
        InvalidateHasChangesCache();
    }

    private TChangeSet GetChangeSetForObservedSnapshot()
    {
        if (_cachedChangeSetValid && _cachedChangeSetGeneration == _observedGeneration && _cachedChangeSetBaselineRevision == _baselineRevision)
        {
            return _cachedChangeSet!;
        }

        var changes = _between(_baseline, _lastObserved);
        _cachedChangeSet = changes;
        _cachedChangeSetGeneration = _observedGeneration;
        _cachedChangeSetBaselineRevision = _baselineRevision;
        _cachedChangeSetValid = true;
        return changes;
    }

    private void OnObservableChanged()
    {
        if (_batchDepth > 0)
        {
            // Defer all snapshot/diff work to the outermost batch exit.
            _batchDirty = true;
            MarkObservedSnapshotInvalid();
            OnPropertyChanged(nameof(HasChanges));
            return;
        }

        MarkObservedSnapshotInvalid();
        var current = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model));
        TChangeSet transition;
        try
        {
            transition = _between(_lastObserved, current);
        }
        catch (InvalidOperationException exception) when (IsDuplicateKeyError(exception))
        {
            OnPropertyChanged(nameof(HasChanges));
            return;
        }

        SetObservedSnapshot(current);

        if (_batchDepth == 0 && !_isEmpty(transition))
        {
            TransitionObserved?.Invoke(transition);
        }

        OnPropertyChanged(nameof(HasChanges));
    }

    private void DisableHasChangesCache()
    {
        _cacheObservableChanges = false;
        InvalidateHasChangesCache();
    }

    private void InvalidateHasChangesCache()
    {
        _hasChangesCacheValid = false;
        _cachedChangeSetValid = false;
    }

    private void CacheHasChanges(TChangeSet changes)
    {
        _cachedHasChanges = !_isEmpty(changes);
        _hasChangesCacheValid = true;
    }

    private void OnObservableMutation()
    {
        OnObservableChanged();
        if (_batchDepth > 0)
        {
            _batchOnChanged = true;
        }
        else
        {
            _onChanged?.Invoke();
        }
    }

    private static bool IsDuplicateKeyError(InvalidOperationException exception) =>
        string.Equals(exception.Message, DuplicateKeyErrorMessage, StringComparison.Ordinal);

    private static bool IsInvalidKeyedStateError(InvalidOperationException exception) =>
        IsDuplicateKeyError(exception)
        || string.Equals(exception.Message, UnassignedBaselineErrorMessage, StringComparison.Ordinal)
        || string.Equals(exception.Message, UnassignedChangeSetBaselineErrorMessage, StringComparison.Ordinal);

    private void OnPropertyChanged(string propertyName)
    {
        if (_batchDepth > 0)
        {
            _batchPropertyChanged = true;
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static void ValidateNotificationDelegates(
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable
    )
    {
        if (fromModel is null)
            throw new ArgumentNullException(nameof(fromModel));
        if (between is null)
            throw new ArgumentNullException(nameof(between));
        if (toPatch is null)
            throw new ArgumentNullException(nameof(toPatch));
        if (isEmpty is null)
            throw new ArgumentNullException(nameof(isEmpty));
        if (advanceBaseline is null)
            throw new ArgumentNullException(nameof(advanceBaseline));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }

    private static void ValidateDelegates(
        Func<TModel, TFragment> fromModel,
        Func<global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, global::SparseTypes.Optional<TFragment?>, global::SparseTypes.Optional<TFragment?>> advanceBaseline,
        Func<TModel, TObservable> toObservable
    )
    {
        if (fromModel is null)
            throw new ArgumentNullException(nameof(fromModel));
        if (between is null)
            throw new ArgumentNullException(nameof(between));
        if (toPatch is null)
            throw new ArgumentNullException(nameof(toPatch));
        if (isEmpty is null)
            throw new ArgumentNullException(nameof(isEmpty));
        if (advanceBaseline is null)
            throw new ArgumentNullException(nameof(advanceBaseline));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }
}
