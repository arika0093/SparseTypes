using System.ComponentModel;

namespace SparseTypes;

/// <summary>Read-only state exposed by an optional edit controller to UI adapters.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditController<TModel>
    where TModel : class
{
    /// <summary>Raised after save or validation state changes.</summary>
    event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the generated session controlled by this instance.</summary>
    ISparseEditSession<TModel> Session { get; }

    /// <summary>Gets the typed validation store for local and server errors.</summary>
    SparseValidationStore<TModel> Validation { get; }

    /// <summary>Gets the current save state.</summary>
    SparseEditSaveState SaveState { get; }

    /// <summary>Gets whether preparation or persistence is in progress.</summary>
    bool IsBusy { get; }

    /// <summary>Gets whether the application may enable editing controls.</summary>
    bool CanEdit { get; }

    /// <summary>Gets whether any registered child session or uninserted collection draft is open.</summary>
    bool HasPendingDrafts { get; }

    /// <summary>Gets whether the session has committed pending changes.</summary>
    bool HasChanges { get; }

    /// <summary>Gets whether the validation store contains errors.</summary>
    bool HasErrors { get; }

    /// <summary>Gets whether the current session can start a save preparation.</summary>
    bool CanSave { get; }
}
