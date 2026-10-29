using System.ComponentModel;

namespace SparseTypes;

/// <summary>Applies the inverse of a typed change set to a current model.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChangeSetReverter
{
    /// <summary>Tries to restore the state before this change set on a model containing its after-state.</summary>
    /// <param name="current">The model to restore from the change set's after-state.</param>
    /// <param name="restored">The restored before-state on success.</param>
    /// <returns>True when the change set could be inverted and applied.</returns>
    bool TryRevert(object current, out object? restored);
}
