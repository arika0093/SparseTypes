using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SparseTypes;

/// <summary>Validation and readiness callbacks for one optional edit controller.</summary>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record SparseEditControllerOptions<TChangeSet>
    where TChangeSet : class
{
    /// <summary>Gets or initializes synchronous validation over the captured change set.</summary>
    public Func<TChangeSet, IReadOnlyList<SparseValidationError>>? Validate { get; init; }

    /// <summary>Gets or initializes asynchronous validation over the captured change set.</summary>
    public Func<TChangeSet, CancellationToken, Task<IReadOnlyList<SparseValidationError>>>? ValidateAsync { get; init; }

    /// <summary>Gets or initializes a live readiness check, or null when the editor is ready.</summary>
    public Func<string?>? ValidateReadiness { get; init; }

    /// <summary>Gets or initializes the origin used for local validation messages.</summary>
    public string ValidationOrigin { get; init; } = "Local";

    /// <summary>Gets or initializes the origin used for server validation messages.</summary>
    public string ServerValidationOrigin { get; init; } = "Server";
}

/// <summary>Reports the state of one save attempt.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseEditSaveState
{
    /// <summary>No save attempt is active.</summary>
    Idle,

    /// <summary>Validation and immutable submission capture are active.</summary>
    Preparing,

    /// <summary>A prepared submission is ready to persist.</summary>
    Ready,

    /// <summary>The persistence callback is active.</summary>
    Saving,

    /// <summary>The submitted change set was persisted and reconciled.</summary>
    Saved,

    /// <summary>The server explicitly rejected the submitted change set.</summary>
    Rejected,

    /// <summary>The persistence outcome is unknown and requires application resolution.</summary>
    OutcomeUnknown,

    /// <summary>The server persisted the change set, but the session has not adopted the result.</summary>
    PersistedButUnreconciled,
}

/// <summary>Describes the persistence callback's authoritative outcome.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparsePersistDisposition
{
    /// <summary>The server explicitly rejected the submission.</summary>
    Rejected,

    /// <summary>The server persisted the submission and returned its authoritative model.</summary>
    Persisted,

    /// <summary>The server outcome cannot be determined.</summary>
    OutcomeUnknown,
}

/// <summary>Typed result returned by an application persistence callback.</summary>
/// <typeparam name="TModel">The authoritative model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record SparsePersistResult<TModel>
    where TModel : class
{
    private SparsePersistResult() { }

    /// <summary>Gets the persistence disposition.</summary>
    public SparsePersistDisposition Disposition { get; private init; }

    /// <summary>Gets the authoritative model returned after successful persistence.</summary>
    public TModel? Model { get; private init; }

    /// <summary>Gets validation errors returned by an explicit rejection.</summary>
    public IReadOnlyList<SparseValidationError> Errors { get; private init; } = Array.Empty<SparseValidationError>();

    /// <summary>Creates a successful persistence result.</summary>
    /// <param name="model">The authoritative persisted model.</param>
    public static SparsePersistResult<TModel> Success(TModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new SparsePersistResult<TModel> { Disposition = SparsePersistDisposition.Persisted, Model = model };
    }

    /// <summary>Creates an explicit rejection result.</summary>
    /// <param name="errors">The server validation errors.</param>
    public static SparsePersistResult<TModel> Reject(IReadOnlyList<SparseValidationError>? errors = null) =>
        new() { Disposition = SparsePersistDisposition.Rejected, Errors = errors ?? Array.Empty<SparseValidationError>() };

    /// <summary>Creates an indeterminate persistence result.</summary>
    public static SparsePersistResult<TModel> Unknown() => new() { Disposition = SparsePersistDisposition.OutcomeUnknown };
}

/// <summary>Immutable change-set capture prepared by one edit controller.</summary>
/// <typeparam name="TModel">The model type.</typeparam>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePreparedSubmission<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    internal SparsePreparedSubmission(
        object owner,
        EditSessionGroupSubmission groupSubmission,
        EditSessionSectionHandle<TModel, TChangeSet> handle,
        TChangeSet changes,
        IReadOnlyList<SparsePath> changedPaths,
        long editGeneration,
        long baselineRevision,
        bool isEmpty
    )
    {
        Owner = owner;
        GroupSubmission = groupSubmission;
        Handle = handle;
        Changes = changes;
        ChangedPaths = changedPaths;
        EditGeneration = editGeneration;
        BaselineRevision = baselineRevision;
        IsEmpty = isEmpty;
    }

    /// <summary>Gets the exact immutable change set validated during preparation.</summary>
    public TChangeSet Changes { get; }

    /// <summary>Gets the changed typed paths captured during preparation.</summary>
    public IReadOnlyList<SparsePath> ChangedPaths { get; }

    /// <summary>Gets the edit-session baseline revision at preparation time.</summary>
    public long BaselineRevision { get; }

    /// <summary>Gets whether the prepared change set contains no changes.</summary>
    public bool IsEmpty { get; }

    internal object Owner { get; }

    internal EditSessionGroupSubmission GroupSubmission { get; }

    internal EditSessionSectionHandle<TModel, TChangeSet> Handle { get; }

    internal long EditGeneration { get; }

    internal bool IsConsumed { get; set; }
}

/// <summary>Optional framework-neutral validation, save, and reconciliation coordination for one edit session.</summary>
/// <remarks>The controller owns no transport or persistence policy. The generated session remains the source of truth.</remarks>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditController<TModel, TChangeSet> : ISparseEditController<TModel>, INotifyPropertyChanged, IDisposable
    where TModel : class
    where TChangeSet : class
{
    private readonly ISparseEditSessionWithChangeSet<TModel, TChangeSet> _session;
    private readonly ISparseEditSessionPipeline<TModel, TChangeSet> _pipeline;
    private readonly ISparseEditSessionWithBaselineRevision? _baseline;
    private readonly ISparseChildSessionRegistry? _childSessions;
    private readonly ISparseCollectionDraftRegistry? _collectionDrafts;
    private readonly INotifyPropertyChanged _notifier;
    private readonly SparseEditControllerOptions<TChangeSet> _options;
    private readonly EditSessionGroup _group = new();
    private readonly EditSessionSectionHandle<TModel, TChangeSet> _handle;
    private long _editGeneration;
    private long _preparingGeneration;
    private int _operationActive;
    private bool _isBusy;
    private bool _disposed;
    private bool _disposePending;
    private SparsePreparedSubmission<TModel, TChangeSet>? _unknownSubmission;
    private SparsePreparedSubmission<TModel, TChangeSet>? _unreconciledSubmission;
    private TModel? _unreconciledModel;
    private string? _reconciliationError;

    /// <summary>Creates an optional controller for a generated edit session.</summary>
    /// <param name="session">The generated session that owns the pending model state.</param>
    /// <param name="options">Validation and readiness callbacks, or null for no application validation.</param>
    /// <exception cref="ArgumentException">The session lacks the generated reconciliation or notification contracts.</exception>
    public SparseEditController(ISparseEditSessionWithChangeSet<TModel, TChangeSet> session, SparseEditControllerOptions<TChangeSet>? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionPipeline<TModel, TChangeSet> pipeline)
        {
            throw new ArgumentException("The generated session does not provide save reconciliation.", nameof(session));
        }

        if (session is not INotifyPropertyChanged notifier)
        {
            throw new ArgumentException("The generated session does not provide change notifications.", nameof(session));
        }

        _session = session;
        _pipeline = pipeline;
        _baseline = session as ISparseEditSessionWithBaselineRevision;
        _childSessions = session as ISparseChildSessionRegistry;
        _collectionDrafts = session as ISparseCollectionDraftRegistry;
        _notifier = notifier;
        _options = options ?? new SparseEditControllerOptions<TChangeSet>();
        ArgumentException.ThrowIfNullOrEmpty(_options.ValidationOrigin);
        ArgumentException.ThrowIfNullOrEmpty(_options.ServerValidationOrigin);
        Validation = new SparseValidationStore<TModel>();
        Validation.ErrorsChanged += OnValidationErrorsChanged;

        var registration = new EditSessionGroupRegistration<TModel, TChangeSet>(session)
        {
            Validate = ValidateReadiness,
            ValidateChanges = ValidateCapturedChanges,
            ValidateAsync = ValidateCapturedChangesAsync,
            TryReconcile = (changes, persisted) =>
            {
                var success = _pipeline.TryReconcileForPipeline(changes, persisted, out var conflicts);
                var message = success
                    ? null
                    : string.Join("; ", conflicts?.Select(static conflict => conflict.Reason ?? conflict.PathText) ?? Array.Empty<string>());
                return (success, message);
            },
        };
        _handle = _group.Register("editor", registration);
        _notifier.PropertyChanged += OnSessionChanged;
        if (_collectionDrafts is not null)
        {
            _collectionDrafts.CollectionDraftsChanged += OnCollectionDraftsChanged;
        }
    }

    /// <summary>Raised after controller or validation state changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the generated session controlled by this instance.</summary>
    public ISparseEditSessionWithChangeSet<TModel, TChangeSet> Session => _session;

    /// <inheritdoc />
    ISparseEditSession<TModel> ISparseEditController<TModel>.Session => _session;

    /// <summary>Gets the typed validation store for local and server errors.</summary>
    public SparseValidationStore<TModel> Validation { get; }

    /// <summary>Gets the current save state.</summary>
    public SparseEditSaveState SaveState { get; private set; }

    /// <summary>Gets whether preparation or persistence is in progress.</summary>
    public bool IsBusy => _isBusy;

    /// <summary>Gets whether the application may enable editing controls.</summary>
    public bool CanEdit => !_isBusy && _unreconciledSubmission is null;

    /// <summary>Gets whether a child session has pending changes or an uninserted collection draft is open.</summary>
    public bool HasPendingDrafts
    {
        get
        {
            var root = SparsePath.Root(typeof(TModel));
            return (_childSessions?.HasPendingChildChangesUnder(root) ?? false) || (_collectionDrafts?.HasPendingCollectionDraftUnder(root) ?? false);
        }
    }

    /// <summary>Gets whether the session has committed pending changes.</summary>
    public bool HasChanges => _session.HasChanges;

    /// <summary>Gets whether the validation store contains errors.</summary>
    public bool HasErrors => Validation.HasErrors;

    /// <summary>Gets whether the current session can start a save preparation.</summary>
    public bool CanSave =>
        !_disposed
        && !_isBusy
        && _unreconciledSubmission is null
        && _unknownSubmission is null
        && !HasPendingDrafts
        && !HasErrors
        && _session.HasChanges;

    /// <summary>Gets the submission whose persistence outcome is unknown, if any.</summary>
    public SparsePreparedSubmission<TModel, TChangeSet>? UnknownSubmission => _unknownSubmission;

    /// <summary>Gets the server-persisted submission that still needs local reconciliation, if any.</summary>
    public SparsePreparedSubmission<TModel, TChangeSet>? UnreconciledSubmission => _unreconciledSubmission;

    /// <summary>Gets the latest reconciliation error message.</summary>
    public string? ReconciliationError => _reconciliationError;

    /// <summary>Captures and validates an immutable change-set submission.</summary>
    /// <param name="cancellationToken">The validation cancellation token.</param>
    /// <returns>The exact submission that can be passed to <see cref="SaveAsync"/>.</returns>
    /// <exception cref="EditSessionGroupValidationException">A draft, readiness check, or validator blocks submission.</exception>
    /// <exception cref="EditSessionStaleSubmissionException">Input changed while asynchronous validation was running.</exception>
    public async Task<SparsePreparedSubmission<TModel, TChangeSet>> PrepareSaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnterOperation();
        SaveState = SparseEditSaveState.Preparing;
        NotifyStateChanged();
        var generation = Interlocked.Read(ref _editGeneration);
        _preparingGeneration = generation;
        try
        {
            var submission = await _group.PrepareAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref _editGeneration) || HasPendingDrafts)
            {
                throw new EditSessionStaleSubmissionException(
                    "The edit session changed or opened a child draft during validation; prepare the submission again."
                );
            }

            var isEmpty = submission.Sections.Count == 0;
            TChangeSet changes;
            IReadOnlyList<SparsePath> changedPaths;
            if (isEmpty)
            {
                changes = _session.CreateChangeSet();
                changedPaths = Array.Empty<SparsePath>();
            }
            else
            {
                var entry = submission.Get(_handle);
                changes = entry.Changes;
                changedPaths = entry.ChangedPaths;
            }

            if (generation != Interlocked.Read(ref _editGeneration) || HasPendingDrafts)
            {
                throw new EditSessionStaleSubmissionException(
                    "The edit session changed or opened a child draft during validation; prepare the submission again."
                );
            }

            SaveState = SparseEditSaveState.Ready;
            return new SparsePreparedSubmission<TModel, TChangeSet>(
                this,
                submission,
                _handle,
                changes,
                changedPaths,
                generation,
                _baseline?.BaselineRevision ?? 0,
                isEmpty
            );
        }
        catch
        {
            SaveState = SparseEditSaveState.Idle;
            throw;
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Persists one prepared submission and reconciles the authoritative response.</summary>
    /// <param name="submission">The immutable submission returned by <see cref="PrepareSaveAsync"/>.</param>
    /// <param name="persist">The application callback that performs persistence.</param>
    /// <param name="cancellationToken">The persistence cancellation token.</param>
    /// <returns>The resulting save state. The callback is not invoked for stale or empty submissions.</returns>
    /// <exception cref="ArgumentException">The submission belongs to another controller.</exception>
    /// <exception cref="EditSessionStaleSubmissionException">The submission was consumed or the session changed before sending.</exception>
    public async Task<SparseEditSaveState> SaveAsync(
        SparsePreparedSubmission<TModel, TChangeSet> submission,
        Func<TChangeSet, CancellationToken, Task<SparsePersistResult<TModel>>> persist,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(persist);
        if (!ReferenceEquals(submission.Owner, this))
        {
            throw new ArgumentException("The prepared submission belongs to another edit controller.", nameof(submission));
        }

        EnterOperation();
        try
        {
            if (submission.IsConsumed)
            {
                throw new EditSessionStaleSubmissionException("The prepared submission has already been consumed.");
            }

            EnsureSubmissionIsCurrent(submission);
            cancellationToken.ThrowIfCancellationRequested();
            submission.IsConsumed = true;
            if (submission.IsEmpty)
            {
                SaveState = SparseEditSaveState.Idle;
                return SaveState;
            }

            SaveState = SparseEditSaveState.Saving;
            NotifyStateChanged();
            SparsePersistResult<TModel> result;
            try
            {
                result =
                    await persist(submission.Changes, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The persistence callback returned no result.");
            }
            catch
            {
                _unknownSubmission = submission;
                SaveState = SparseEditSaveState.OutcomeUnknown;
                throw;
            }

            switch (result.Disposition)
            {
                case SparsePersistDisposition.Rejected:
                    SaveState = SparseEditSaveState.Rejected;
                    ReplaceErrors(_options.ServerValidationOrigin, result.Errors);
                    return SaveState;
                case SparsePersistDisposition.OutcomeUnknown:
                    _unknownSubmission = submission;
                    SaveState = SparseEditSaveState.OutcomeUnknown;
                    return SaveState;
                case SparsePersistDisposition.Persisted:
                    if (result.Model is null)
                    {
                        _unknownSubmission = submission;
                        SaveState = SparseEditSaveState.OutcomeUnknown;
                        throw new InvalidOperationException("A persisted result must include its authoritative model.");
                    }

                    bool reconciled;
                    string? error;
                    try
                    {
                        reconciled = TryReconcile(submission, result.Model, out error);
                    }
                    catch (Exception exception)
                    {
                        _unreconciledSubmission = submission;
                        _unreconciledModel = result.Model;
                        _reconciliationError = exception.Message;
                        SaveState = SparseEditSaveState.PersistedButUnreconciled;
                        throw;
                    }

                    if (reconciled)
                    {
                        ReplaceErrors(_options.ServerValidationOrigin, Array.Empty<SparseValidationError>());
                        SaveState = SparseEditSaveState.Saved;
                        return SaveState;
                    }

                    _unreconciledSubmission = submission;
                    _unreconciledModel = result.Model;
                    _reconciliationError = error;
                    SaveState = SparseEditSaveState.PersistedButUnreconciled;
                    return SaveState;
                default:
                    throw new InvalidOperationException($"Unknown persistence disposition '{result.Disposition}'.");
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Retries local reconciliation without resubmitting an already persisted change set.</summary>
    /// <returns>True when the authoritative result was adopted.</returns>
    public bool RetryReconciliation()
    {
        ThrowIfDisposed();
        if (_unreconciledSubmission is null || _unreconciledModel is null)
        {
            return false;
        }

        EnterOperation();
        try
        {
            if (!TryReconcile(_unreconciledSubmission, _unreconciledModel, out var error))
            {
                _reconciliationError = error;
                return false;
            }

            _unreconciledSubmission = null;
            _unreconciledModel = null;
            _reconciliationError = null;
            ReplaceErrors(_options.ServerValidationOrigin, Array.Empty<SparseValidationError>());
            SaveState = SparseEditSaveState.Saved;
            return true;
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Clears an indeterminate result only after the application has established that it was not persisted.</summary>
    /// <exception cref="InvalidOperationException">There is no indeterminate result to resolve.</exception>
    public void ResolveUnknownOutcomeAsNotPersisted()
    {
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            if (_unknownSubmission is null)
            {
                throw new InvalidOperationException("There is no unknown persistence outcome to resolve.");
            }

            _unknownSubmission = null;
            SaveState = SparseEditSaveState.Idle;
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Adopts an authoritative model after the application resolves an indeterminate persistence result.</summary>
    /// <param name="persisted">The authoritative model obtained from the application or server.</param>
    /// <returns>True when the submitted changes were reconciled.</returns>
    public bool ResolveUnknownOutcomeAsPersisted(TModel persisted)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(persisted);
        EnterOperation();
        try
        {
            if (_unknownSubmission is null)
            {
                throw new InvalidOperationException("There is no unknown persistence outcome to resolve.");
            }

            if (!TryReconcile(_unknownSubmission, persisted, out var error))
            {
                _unreconciledSubmission = _unknownSubmission;
                _unreconciledModel = persisted;
                _reconciliationError = error;
                _unknownSubmission = null;
                SaveState = SparseEditSaveState.PersistedButUnreconciled;
                return false;
            }

            _unknownSubmission = null;
            ReplaceErrors(_options.ServerValidationOrigin, Array.Empty<SparseValidationError>());
            SaveState = SparseEditSaveState.Saved;
            return true;
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _editGeneration);
        _notifier.PropertyChanged -= OnSessionChanged;
        Validation.ErrorsChanged -= OnValidationErrorsChanged;
        if (_collectionDrafts is not null)
        {
            _collectionDrafts.CollectionDraftsChanged -= OnCollectionDraftsChanged;
        }

        if (Volatile.Read(ref _operationActive) != 0)
        {
            _disposePending = true;
        }
        else
        {
            Validation.Dispose();
        }
    }

    private string? ValidateReadiness()
    {
        if (HasPendingDrafts)
        {
            return "A child session or uninserted collection draft is still open.";
        }

        return _options.ValidateReadiness?.Invoke();
    }

    private string? ValidateCapturedChanges(TChangeSet changes)
    {
        if (_options.Validate is null)
        {
            ReplaceErrors(_options.ValidationOrigin, Array.Empty<SparseValidationError>());
            return null;
        }

        var errors = _options.Validate(changes) ?? throw new InvalidOperationException("The validation callback returned no error list.");
        ReplaceErrors(_options.ValidationOrigin, errors);
        return errors.Count == 0 ? null : "The captured change set has validation errors.";
    }

    private async Task<string?> ValidateCapturedChangesAsync(TChangeSet changes, CancellationToken cancellationToken)
    {
        if (_options.ValidateAsync is null)
        {
            return null;
        }

        var errors = await _options.ValidateAsync(changes, cancellationToken).ConfigureAwait(false);
        if (_preparingGeneration != Interlocked.Read(ref _editGeneration) || HasPendingDrafts)
        {
            return "The validation result is stale because the edit session changed.";
        }

        if (errors is null)
        {
            throw new InvalidOperationException("The asynchronous validation callback returned no error list.");
        }

        ReplaceErrors(_options.ValidationOrigin, errors);
        return errors.Count == 0 ? null : "The captured change set has validation errors.";
    }

    private bool TryReconcile(SparsePreparedSubmission<TModel, TChangeSet> submission, TModel persisted, out string? error)
    {
        var success = _group.TryReconcile(submission.GroupSubmission, submission.Handle, persisted, out error);
        return success;
    }

    private void EnsureSubmissionIsCurrent(SparsePreparedSubmission<TModel, TChangeSet> submission)
    {
        if (
            submission.EditGeneration != Interlocked.Read(ref _editGeneration)
            || (_baseline is not null && submission.BaselineRevision != _baseline.BaselineRevision)
            || HasPendingDrafts
        )
        {
            SaveState = SparseEditSaveState.Idle;
            NotifyStateChanged();
            throw new EditSessionStaleSubmissionException("The prepared submission is stale; capture and validate the current edit state again.");
        }
    }

    private void ReplaceErrors(string origin, IReadOnlyList<SparseValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var grouped = new Dictionary<SparsePath, List<object?>>();
        foreach (var error in errors)
        {
            if (error.Path.RootType != typeof(TModel))
            {
                throw new ArgumentException($"Validation path '{error.Path}' belongs to a different model.", nameof(errors));
            }

            if (!grouped.TryGetValue(error.Path, out var pathErrors))
            {
                pathErrors = [];
                grouped.Add(error.Path, pathErrors);
            }

            pathErrors.Add(error.Error);
        }

        Validation.ClearOrigin(origin);
        foreach (var entry in grouped)
        {
            Validation.SetErrors(entry.Key, origin, entry.Value);
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        Interlocked.Increment(ref _editGeneration);
        ReplaceErrors(_options.ValidationOrigin, Array.Empty<SparseValidationError>());
        ReplaceErrors(_options.ServerValidationOrigin, Array.Empty<SparseValidationError>());
        if (!_isBusy && SaveState is SparseEditSaveState.Ready or SparseEditSaveState.Rejected or SparseEditSaveState.Saved)
        {
            SaveState = SparseEditSaveState.Idle;
        }

        NotifyStateChanged();
    }

    private void OnCollectionDraftsChanged()
    {
        Interlocked.Increment(ref _editGeneration);
        ReplaceErrors(_options.ValidationOrigin, Array.Empty<SparseValidationError>());
        ReplaceErrors(_options.ServerValidationOrigin, Array.Empty<SparseValidationError>());
        NotifyStateChanged();
    }

    private void OnValidationErrorsChanged(object? sender, DataErrorsChangedEventArgs args) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveState)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSave)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChanges)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPendingDrafts)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnknownSubmission)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreconciledSubmission)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReconciliationError)));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    private void EnterOperation()
    {
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The edit controller is already preparing or saving.");
        }

        _isBusy = true;
        NotifyStateChanged();
    }

    private void ExitOperation()
    {
        _isBusy = false;
        Interlocked.Exchange(ref _operationActive, 0);
        if (_disposePending)
        {
            Validation.Dispose();
        }

        if (!_disposed)
        {
            NotifyStateChanged();
        }
    }
}
