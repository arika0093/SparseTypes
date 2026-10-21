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

/// <summary>Speculative branches that share a lineage identifier.</summary>
/// <remarks>Forks snapshot the current model; merges rebase fork transitions onto the receiver.</remarks>
public partial class EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
{
    /// <summary>Creates an independent speculative branch of this session.</summary>
    /// <remarks>The fork snapshots the current model as its baseline.</remarks>
    /// <returns>A new session of the same runtime type with its own live model.</returns>
    /// <exception cref="NotSupportedException">
    /// The session was built without baseline materialization support, or the model
    /// graph cannot be snapshotted (such as a cyclic graph).
    /// </exception>
    public virtual EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> Fork()
    {
        // ToModel aliases its source fragment, so the live copy needs its
        // own snapshot to stay independent of the fork baseline.
        var forkPoint = _fromModel(_model);
        var forkLive = CloneLiveModel(_model);
        return CreateBranched(forkLive, global::SparseTypes.Optional<TFragment?>.Present(forkPoint), _rootId, onChanged: null);
    }

    /// <summary>Attempts to merge the changes of a fork into this session without modifying either session.</summary>
    /// <remarks>The fork's changes are rebased onto this session's current model.</remarks>
    /// <param name="fork">A session created by <see cref="Fork"/> in the same lineage.</param>
    /// <param name="merged">The new session with this session's baseline and the merged model, or null on failure.</param>
    /// <param name="conflicts">Structured rebase conflicts on failure; otherwise null.</param>
    /// <returns><see langword="true"/> when the fork merged cleanly; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fork"/> is
    /// null.</exception>
    /// <exception cref="InvalidOperationException">The fork is invalid for this session.</exception>
    /// <exception cref="NotSupportedException">
    /// Either session was built without rebase or baseline materialization support,
    /// or the merged model graph cannot be snapshotted.
    /// </exception>
    public virtual bool TryMergeFrom(
        EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> fork,
        out EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>? merged,
        out IReadOnlyList<global::SparseTypes.SparseConflict>? conflicts
    )
    {
        if (fork is null)
            throw new ArgumentNullException(nameof(fork));
        if (ReferenceEquals(this, fork))
            throw new InvalidOperationException("Cannot merge an edit session into itself. Fork the session first and merge the fork.");
        if (fork._rootId != _rootId)
            throw new InvalidOperationException(
                "Cannot merge an edit session from an unrelated branch lineage. Only sessions created by Fork share a lineage."
            );
        if (_rebase is null || _tryApplyTo is null || _baselineToModel is null)
        {
            throw new NotSupportedException(
                "This edit session cannot merge forks because it was built without rebase or baseline materialization support."
            );
        }

        if (!_baseline.IsPresent || _baseline.Value is null)
            throw new InvalidOperationException("Cannot merge into an edit session with a missing baseline.");

        // Fork changes B -> D; invalid keyed state throws before anything
        // is published.
        var forkChanges = fork.CreateChangeSet();
        var rebased = _rebase(forkChanges, _model);
        if (rebased.HasConflicts)
        {
            merged = null;
            conflicts = rebased.Conflicts;
            return false;
        }

        var applied = _tryApplyTo(rebased.Rebased, _model);
        if (applied.Conflicts is not null)
        {
            merged = null;
            conflicts = applied.Conflicts;
            return false;
        }

        if (applied.Updated is null)
        {
            throw new InvalidOperationException("Merging the fork did not produce a model.");
        }

        // Clone through the fragment snapshot to detach mutable references.
        var detached = CloneLiveModel(applied.Updated);
        // Fail fast when the merged state cannot serve as a future diff
        // baseline.
        _between(_baseline, global::SparseTypes.Optional<TFragment?>.Present(_fromModel(detached)));

        merged = CreateBranched(detached, _baseline, _rootId, _onChanged);
        conflicts = null;
        return true;
    }

    /// <summary>Creates a session of this runtime type with an explicit baseline and lineage.</summary>
    /// <param name="live">The independent live model for the new session.</param>
    /// <param name="baseline">The retained baseline snapshot.</param>
    /// <param name="rootId">The branch lineage to continue.</param>
    /// <param name="onChanged">The change callback for the new session.</param>
    /// <returns>A new session with fresh bindings.</returns>
    protected virtual EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> CreateBranched(
        TModel live,
        global::SparseTypes.Optional<TFragment?> baseline,
        Guid rootId,
        Action? onChanged
    )
    {
        if (live is null)
            throw new ArgumentNullException(nameof(live));
        if (!baseline.IsPresent || baseline.Value is null)
            throw new InvalidOperationException("Cannot branch an edit session with a missing baseline.");
        var branched = new EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            live,
            baseline.Value,
            _fromModel,
            _between,
            _toPatch,
            _isEmpty,
            _advanceBaseline,
            _toObservable,
            onChanged,
            _tryApplyTo,
            _writeModel,
            _invert,
            _rebase,
            _enumerateChangedPaths,
            _refreshObservable,
            _initialCacheObservableChanges,
            _baselineToModel
        );
        branched.AdoptBranchRoot(rootId);
        return branched;
    }

    internal void AdoptBranchRoot(Guid rootId) => _rootId = rootId;

    /// <summary>Determines whether another session belongs to the same branch lineage.</summary>
    /// <param name="other">The session to compare.</param>
    /// <returns><see langword="true"/> when both sessions share a branch root.</returns>
    public bool IsInSameLineage(EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable> other) =>
        other is not null && _rootId == other._rootId;

    /// <summary>Refreshes observable state after the live model was mutated outside the session.</summary>
    /// <remarks>Generated child-draft hosts call this after writing through child-session paths.</remarks>
    public void RefreshAfterExternalMutation() => RefreshAfterModelMutation();
}
