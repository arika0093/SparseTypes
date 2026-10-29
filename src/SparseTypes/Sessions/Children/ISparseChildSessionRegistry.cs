using System.ComponentModel;

namespace SparseTypes;

/// <summary>Queries registered child edit sessions and their pending changes.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChildSessionRegistry
{
    /// <summary>Determines whether a child edit session is registered at or below a path.</summary>
    /// <param name="path">The path whose subtree would be replaced or removed.</param>
    bool HasPendingChildSessionUnder(SparsePath path);

    /// <summary>Determines whether a registered child session at or below a path has pending changes.</summary>
    /// <param name="path">The path whose subtree is being checked.</param>
    bool HasPendingChildChangesUnder(SparsePath path);
}
