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

/// <summary>In-place application, revert, and reload over the retained baseline.</summary>
/// <remarks>Applies change sets to the live model in place; conflicts leave the model unchanged.</remarks>
public partial class EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
{
    /// <summary>Applies a change set in place after checking its before-state.</summary>
    /// <param name="changes">The transition to apply.</param>
    /// <param name="conflicts">Structured conflicts when the before-state cannot be applied.</param>
    /// <returns><see langword="true"/> when the model was updated; otherwise <see langword="false"/>.</returns>
    /// <remarks>Conflicts leave the live model unchanged and return details.</remarks>
    public bool TryApplyInPlace(TChangeSet changes, out IReadOnlyList<global::SparseTypes.SparseConflict>? conflicts)
    {
        if (changes is null)
            throw new ArgumentNullException(nameof(changes));
        if (_tryApplyInPlace is not null)
        {
            (bool Succeeded, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts) directResult;
            try
            {
                directResult = _tryApplyInPlace(changes, _model);
            }
            catch
            {
                ResynchronizeAfterFailedMutation();
                throw;
            }

            if (!directResult.Succeeded)
            {
                conflicts = directResult.Conflicts;
                return false;
            }

            conflicts = null;
            RefreshAfterModelMutation();
            return true;
        }

        if (_tryApplyTo is null || _writeModel is null)
        {
            throw new NotSupportedException("This model cannot be updated in place.");
        }

        var result = _tryApplyTo(changes, _model);
        if (result.Conflicts is not null)
        {
            conflicts = result.Conflicts;
            return false;
        }

        if (result.Updated is null)
        {
            throw new InvalidOperationException("Applying the change set did not produce a model.");
        }

        var committed = false;
        try
        {
            _writeModel(_model, result.Updated);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                // The model may be partially mutated.
                ResynchronizeAfterFailedMutation();
            }
        }
        conflicts = null;
        RefreshAfterModelMutation();
        return true;
    }

    /// <summary>Applies a change set in place or throws when its before-state conflicts.</summary>
    /// <remarks>See <see cref="TryApplyInPlace"/> for the failure contract.</remarks>
    public void ApplyInPlace(TChangeSet changes)
    {
        if (!TryApplyInPlace(changes, out _))
        {
            throw new InvalidOperationException("The change set cannot be applied to the current model because its before-state conflicts.");
        }
    }

    /// <summary>Reverts the current model to its retained baseline in place.</summary>
    /// <remarks>When the live model is temporarily invalid (duplicate keyed keys or unassigned keyed sentinels) it cannot be diffed.</remarks>
    public void RevertChanges()
    {
        if (!TryRevertChanges(out _))
        {
            throw new InvalidOperationException(
                "The pending changes cannot be reverted in place because their "
                    + "before-state conflicts or they include members that cannot be "
                    + "written to an existing model. Use TryRevertChanges for structured "
                    + "conflicts."
            );
        }
    }

    /// <summary>Reverts the current model to its retained baseline when possible in place.</summary>
    /// <param name="conflicts">Structured conflicts when the pending changes cannot be reverted in place.</param>
    /// <returns><see langword="true"/> when the model was reverted; otherwise <see langword="false"/>.</returns>
    /// <remarks>A <see langword="false"/> result leaves both the live model and the retained baseline untouched.</remarks>
    public bool TryRevertChanges(out IReadOnlyList<global::SparseTypes.SparseConflict>? conflicts)
    {
        if (_invert is null)
        {
            throw new NotSupportedException("This edit session cannot invert its change sets.");
        }

        TChangeSet changes;
        try
        {
            changes = CreateChangeSet();
        }
        catch (InvalidOperationException exception) when (IsInvalidKeyedStateError(exception))
        {
            RevertFromInvalidKeyedState(exception);
            conflicts = null;
            return true;
        }

        if (_isEmpty(changes))
        {
            conflicts = null;
            return true;
        }

        TChangeSet inverted;
        try
        {
            inverted = _invert(changes);
        }
        catch (InvalidOperationException exception) when (IsInvalidKeyedStateError(exception))
        {
            RevertFromInvalidKeyedState(exception);
            conflicts = null;
            return true;
        }

        try
        {
            return TryApplyInPlace(inverted, out conflicts);
        }
        catch (InvalidOperationException exception) when (IsInvalidKeyedStateError(exception))
        {
            RevertFromInvalidKeyedState(exception);
            conflicts = null;
            return true;
        }
    }

    /// <summary>Reloads authoritative server state and rebases pending local edits onto it.</summary>
    /// <remarks>Conflicts leave both the live model and retained baseline untouched.</remarks>
    public global::SparseTypes.RebaseResult<TChangeSet> Reload(TModel serverState)
    {
        if (serverState is null)
            throw new ArgumentNullException(nameof(serverState));
        if (_rebase is null || _tryApplyTo is null || _writeModel is null)
        {
            throw new NotSupportedException("This model cannot be reloaded in place.");
        }

        var serverBaseline = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(serverState));
        _between(serverBaseline, _baseline);
        var rebased = _rebase(CreateChangeSet(), serverState);
        if (rebased.HasConflicts)
        {
            return rebased;
        }

        var applied = _tryApplyTo(rebased.Rebased, serverState);
        if (applied.Conflicts is not null)
        {
            return new global::SparseTypes.RebaseResult<TChangeSet>(rebased.Rebased, applied.Conflicts);
        }

        if (applied.Updated is null)
        {
            throw new InvalidOperationException("Reloading did not produce a model.");
        }

        // The in-place writer copies only writable members.
        try
        {
            var drift = _between(
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model)),
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(applied.Updated))
            );
            if (!_isEmpty(drift))
            {
                var probe = _tryApplyTo(drift, _model);
                if (probe.Conflicts is not null)
                {
                    return new global::SparseTypes.RebaseResult<TChangeSet>(rebased.Rebased, probe.Conflicts);
                }
            }
        }
        catch (InvalidOperationException exception) when (IsInvalidKeyedStateError(exception))
        {
            // The probe baseline cannot be diffed.
        }

        try
        {
            _writeModel(_model, applied.Updated);
        }
        catch
        {
            // The writer runs arbitrary setters and may have partially
            // mutated the live model.
            ResynchronizeAfterFailedMutation();
            throw;
        }

        SetBaseline(serverBaseline);
        RefreshAfterModelMutation();
        return rebased;
    }

    private void RefreshAfterModelMutation()
    {
        MarkObservedSnapshotInvalid();
        var current = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model));
        var transition = _between(_lastObserved, current);
        SetObservedSnapshot(current);
        _refreshObservable?.Invoke(_observable);
        if (_batchDepth == 0 && !_isEmpty(transition))
        {
            TransitionObserved?.Invoke(transition);
        }

        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Resynchronizes observations after arbitrary user code partially mutated the live model and threw.</summary>
    /// <remarks>Refreshes the observation snapshot.</remarks>
    private void ResynchronizeAfterFailedMutation()
    {
        try
        {
            RefreshAfterModelMutation();
        }
        catch
        {
            // Keep the last valid observation so later diffs still start
            // from valid state.
            InvalidateHasChangesCache();
            try
            {
                _refreshObservable?.Invoke(_observable);
            }
            catch
            {
                // Notification failures yield to the original exception.
            }

            try
            {
                OnPropertyChanged(nameof(HasChanges));
            }
            catch
            {
                // Notification failures yield to the original exception.
            }
        }
    }

    private void RevertFromInvalidKeyedState(InvalidOperationException exception)
    {
        if (_writeModel is null || _baselineToModel is null || !_baseline.IsPresent || _baseline.Value is null)
        {
            throw new InvalidOperationException(
                "The live model cannot be diffed because its keyed state is invalid, "
                    + "and this session cannot materialize its baseline for recovery.",
                exception
            );
        }

        _writeModel(_model, _baselineToModel(_baseline.Value));
        RefreshAfterModelMutation();
    }
}
