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

/// <summary>Submission reconciliation against authoritative persisted models.</summary>
/// <remarks>Rebases submitted and post-submit transitions onto persisted state.</remarks>
public partial class EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
{
    /// <summary>Reconciles a submitted change set with the authoritative persisted model.</summary>
    /// <remarks>Matches submitted and persisted elements by temporary key.</remarks>
    /// <param name="submitted">The submitted transition to acknowledge.</param>
    /// <param name="persisted">The authoritative persisted model.</param>
    /// <param name="buildMap">Correlates submitted and persisted models into an opaque map or reports the first mismatch.</param>
    /// <param name="retarget">Assigns mapped keys into the live model from the submitted, persisted, and current states.</param>
    /// <param name="error">The failure reason, or null on success.</param>
    /// <typeparam name="TMap">The opaque temporary-to-permanent correlation.</typeparam>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The session was built without baseline materialization or in-place write support.
    /// </exception>
    public bool TryReconcile<TMap>(
        TChangeSet submitted,
        TModel persisted,
        Func<TModel, TModel, (TMap? Map, string? Error)> buildMap,
        Func<TModel, TModel, TModel, TMap?, TChangeSet, string?> retarget,
        out string? error
    )
        where TMap : class
    {
        if (submitted is null)
            throw new ArgumentNullException(nameof(submitted));
        if (persisted is null)
            throw new ArgumentNullException(nameof(persisted));
        if (buildMap is null)
            throw new ArgumentNullException(nameof(buildMap));
        if (retarget is null)
            throw new ArgumentNullException(nameof(retarget));
        if (_baselineToModel is null || _writeModel is null)
        {
            throw new NotSupportedException(
                "This edit session cannot reconcile submissions because it was built without baseline materialization or in-place write support."
            );
        }

        error = null;
        var persistedBaseline = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(persisted));

        // Re-acknowledging an already-reconciled state is a no-op success
        // only for a verified submission.
        try
        {
            var reconcileCurrent = _between(persistedBaseline, global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model)));
            if (_isEmpty(reconcileCurrent))
            {
                if (ReferenceEquals(submitted, _lastReconciledSubmission))
                {
                    SetBaseline(persistedBaseline);
                    SetObservedSnapshot(persistedBaseline);
                    RefreshAfterModelMutation();
                    error = null;
                    return true;
                }

                var verifiedAdvanced = _advanceBaseline(submitted, _baseline);
                if (verifiedAdvanced.IsPresent)
                {
                    var verifiedValue = verifiedAdvanced.Value;
                    if (verifiedValue is null)
                    {
                        error = "Advancing the baseline did not produce a valid model state.";
                        return false;
                    }

                    var verifiedAfter = _baselineToModel(verifiedValue);
                    var verifiedPending = _between(
                        global::SparseTypes.Optional<TFragment?>.Present(_fromModel(verifiedAfter)),
                        global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model))
                    );
                    if (_isEmpty(verifiedPending))
                    {
                        SetBaseline(persistedBaseline);
                        SetObservedSnapshot(persistedBaseline);
                        _lastReconciledSubmission = submitted;
                        RefreshAfterModelMutation();
                        error = null;
                        return true;
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
            // The fast-path probe failed; fall through to the full reconstruction below.
        }

        // Reconstruct the submitted-after state; a stale or incompatible
        // submission fails here before anything is mutated.
        TModel submittedAfter;
        try
        {
            var advanced = _advanceBaseline(submitted, _baseline);
            if (!advanced.IsPresent || advanced.Value is null)
            {
                error = "Advancing the baseline did not produce a valid model state.";
                return false;
            }

            submittedAfter = _baselineToModel(advanced.Value);
        }
        catch (InvalidOperationException exception)
        {
            error = "The submitted change set is stale or incompatible with the retained baseline: " + exception.Message;
            return false;
        }

        // The persisted model must diff cleanly; duplicates or otherwise
        // invalid keyed state fail before the live model is touched.
        try
        {
            _ = _between(_baseline, persistedBaseline);
        }
        catch (InvalidOperationException exception)
        {
            error = "The persisted model cannot serve as a keyed baseline: " + exception.Message;
            return false;
        }

        // Post-submit local changes, diffed while temporary identities
        // still align.
        TChangeSet pending;
        try
        {
            pending = _between(
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(submittedAfter)),
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model))
            );
        }
        catch (InvalidOperationException exception)
        {
            error = "The current model cannot be diffed against the submitted state: " + exception.Message;
            return false;
        }

        // Correlate before mutating; mismatches leave all state untouched.
        var (map, mapError) = buildMap(submittedAfter, persisted);
        if (mapError is not null || map is null)
        {
            error = mapError ?? "Temporary correlation did not produce a mapping.";
            return false;
        }

        var backup = CloneLiveModel(_model);
        var backupSnapshot = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(backup));
        string? retargetError;
        try
        {
            retargetError = retarget(_model, submittedAfter, persisted, map, pending);
        }
        catch
        {
            // An arbitrary retarget failure may have partially mutated the
            // live graph.
            try
            {
                RestoreReconcileBackup(backup, backupSnapshot);
            }
            catch
            {
                // Preserve the original retarget failure; the resync below
                // still makes later reads observe the live state.
            }

            ResynchronizeAfterFailedMutation();
            throw;
        }

        if (retargetError is not null)
        {
            try
            {
                RestoreReconcileBackup(backup, backupSnapshot);
            }
            catch
            {
                // The rollback writer runs arbitrary setters.
                ResynchronizeAfterFailedMutation();
                throw;
            }

            RefreshAfterModelMutation();
            error = retargetError;
            return false;
        }

        // The rewritten live model must diff cleanly against the persisted
        // baseline; anything else rolls back instead of retaining damage.
        try
        {
            _ = _between(persistedBaseline, global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model)));
        }
        catch (InvalidOperationException exception)
        {
            try
            {
                _writeModel(_model, backup);
            }
            catch
            {
                // The rollback writer runs arbitrary setters.
                ResynchronizeAfterFailedMutation();
                throw;
            }

            RefreshAfterModelMutation();
            error = "Reconciling produced a model that cannot serve as a keyed baseline: " + exception.Message;
            return false;
        }

        SetBaseline(persistedBaseline);
        // Observe post-submit edits from the persisted baseline so the
        // published transition spans exactly what remains pending.
        SetObservedSnapshot(persistedBaseline);
        _lastReconciledSubmission = submitted;
        RefreshAfterModelMutation();
        return true;
    }

    /// <summary>Reconciles a submission, adopts authoritative values, and preserves post-submit edits.</summary>
    /// <remarks>Temporary identities are correlated before the pending transition is rebased onto persisted state. Conflicting transitions leave the live model and retained baseline unchanged.</remarks>
    /// <param name="submitted">The transition sent to persistence.</param>
    /// <param name="persisted">The authoritative persisted model.</param>
    /// <param name="buildMap">Correlates submitted and persisted models into an opaque map or reports the first mismatch.</param>
    /// <param name="retarget">Applies temporary-key correlation to a staged model.</param>
    /// <param name="createReconcileConflict">Creates a structured conflict for a reconciliation planning failure.</param>
    /// <param name="conflicts">Typed-path conflicts when reconciliation cannot be applied.</param>
    /// <typeparam name="TMap">The opaque temporary-to-permanent correlation.</typeparam>
    /// <returns><see langword="true"/> when persisted values were adopted; otherwise <see langword="false"/>.</returns>
    public bool TryReconcileAndApply<TMap>(
        TChangeSet submitted,
        TModel persisted,
        Func<TModel, TModel, (TMap? Map, string? Error)> buildMap,
        Func<TModel, TModel, TModel, TMap?, TChangeSet?, string?> retarget,
        Func<string, global::SparseTypes.SparseConflict> createReconcileConflict,
        out IReadOnlyList<global::SparseTypes.SparseConflict>? conflicts
    )
        where TMap : class
    {
        if (submitted is null)
            throw new ArgumentNullException(nameof(submitted));
        if (persisted is null)
            throw new ArgumentNullException(nameof(persisted));
        if (buildMap is null)
            throw new ArgumentNullException(nameof(buildMap));
        if (retarget is null)
            throw new ArgumentNullException(nameof(retarget));
        if (createReconcileConflict is null)
            throw new ArgumentNullException(nameof(createReconcileConflict));
        if (_baselineToModel is null || _writeModel is null || _tryApplyTo is null || _rebase is null)
        {
            throw new NotSupportedException(
                "This edit session cannot reconcile submissions because it was built without baseline materialization, rebase, or in-place write support."
            );
        }

        conflicts = null;
        var persistedBaseline = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(persisted));

        TModel submittedAfter;
        try
        {
            var advanced = _advanceBaseline(submitted, _baseline);
            if (!advanced.IsPresent || advanced.Value is null)
            {
                return Fail("Advancing the baseline did not produce a valid model state.", out conflicts);
            }

            submittedAfter = _baselineToModel(advanced.Value);
            _ = _between(_baseline, persistedBaseline);
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The submitted change set or persisted model is stale or incompatible: " + exception.Message, out conflicts);
        }

        TChangeSet pending;
        try
        {
            pending = _between(
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(submittedAfter)),
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model))
            );
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The current model cannot be diffed against the submitted state: " + exception.Message, out conflicts);
        }

        var (map, mapError) = buildMap(submittedAfter, persisted);
        if (mapError is not null || map is null)
        {
            return Fail(mapError ?? "Temporary correlation did not produce a mapping.", out conflicts);
        }

        TModel mappedSubmitted;
        TModel mappedCurrent;
        try
        {
            mappedSubmitted = CloneLiveModel(submittedAfter);
            mappedCurrent = CloneLiveModel(_model);
            var submittedError = retarget(mappedSubmitted, submittedAfter, persisted, map, null);
            if (submittedError is not null)
            {
                return Fail(submittedError, out conflicts);
            }

            var currentError = retarget(mappedCurrent, submittedAfter, persisted, map, pending);
            if (currentError is not null)
            {
                return Fail(currentError, out conflicts);
            }
        }
        catch (InvalidOperationException exception)
        {
            return Fail("Temporary identities could not be reconciled: " + exception.Message, out conflicts);
        }

        TChangeSet mappedPending;
        try
        {
            mappedPending = _between(
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(mappedSubmitted)),
                global::SparseTypes.Optional<TFragment?>.Present(_fromModel(mappedCurrent))
            );
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The retargeted model cannot be diffed: " + exception.Message, out conflicts);
        }

        global::SparseTypes.RebaseResult<TChangeSet> rebased;
        try
        {
            rebased = _rebase(mappedPending, persisted);
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The post-submit changes could not be rebased: " + exception.Message, out conflicts);
        }

        if (rebased.HasConflicts)
        {
            conflicts = rebased.Conflicts;
            return false;
        }

        (TModel? Updated, IReadOnlyList<global::SparseTypes.SparseConflict>? Conflicts) applied;
        try
        {
            applied = _tryApplyTo(rebased.Rebased, persisted);
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The rebased changes could not be applied: " + exception.Message, out conflicts);
        }

        if (applied.Conflicts is not null)
        {
            conflicts = applied.Conflicts;
            return false;
        }

        if (applied.Updated is null)
        {
            return Fail("Applying the post-submit changes did not produce a model.", out conflicts);
        }

        try
        {
            _ = _between(persistedBaseline, global::SparseTypes.Optional<TFragment?>.Present(_fromModel(applied.Updated)));
        }
        catch (InvalidOperationException exception)
        {
            return Fail("The reconciled model cannot serve as a keyed baseline: " + exception.Message, out conflicts);
        }

        var backup = CloneLiveModel(_model);
        var backupSnapshot = global::SparseTypes.Optional<TFragment?>.Present(_fromModel(backup));
        try
        {
            _writeModel(_model, applied.Updated);
        }
        catch
        {
            try
            {
                RestoreReconcileBackup(backup, backupSnapshot);
            }
            catch
            {
                // Preserve the original setter failure.
            }

            ResynchronizeAfterFailedMutation();
            throw;
        }

        SetBaseline(persistedBaseline);
        SetObservedSnapshot(persistedBaseline);
        _lastReconciledSubmission = submitted;
        RefreshAfterModelMutation();
        conflicts = null;
        return true;

        bool Fail(string reason, out IReadOnlyList<global::SparseTypes.SparseConflict>? result)
        {
            result = new[] { createReconcileConflict(reason) };
            return false;
        }
    }

    /// <summary>Restores the live model from a reconcile backup when it diverged.</summary>
    /// <remarks>Only rewrites when the live model actually diverged.</remarks>
    private void RestoreReconcileBackup(TModel backup, global::SparseTypes.Optional<TFragment?> backupSnapshot)
    {
        var restoreBackup = true;
        try
        {
            restoreBackup = !_isEmpty(_between(backupSnapshot, global::SparseTypes.Optional<TFragment?>.Present(_fromModel(_model))));
        }
        catch (InvalidOperationException)
        {
            restoreBackup = true;
        }

        if (restoreBackup)
        {
            // Callers verify in-place write support up front; the bang only
            // reflects flow analysis that does not cross this helper.
            _writeModel!(_model, backup);
        }
    }

    private TModel CloneLiveModel(TModel source)
    {
        if (_baselineToModel is null)
        {
            throw new NotSupportedException("This edit session cannot copy its model because it was built without baseline materialization support.");
        }

        // Snapshots isolate containers and nested models; reference-safe
        // members stay shared.
        return _baselineToModel(_fromModel(source));
    }
}
