using System;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Tracks uninserted collection-item drafts owned by an edit session.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseCollectionDraftRegistry
{
    /// <summary>Gets whether an uninserted collection-item draft is open.</summary>
    bool HasPendingCollectionDrafts { get; }

    /// <summary>Occurs when the set of uninserted collection-item drafts changes.</summary>
    event Action? CollectionDraftsChanged;

    /// <summary>Registers an uninserted collection-item draft with this session.</summary>
    /// <param name="path">The collection path that will receive the item.</param>
    /// <param name="draft">The draft handle used to release it on explicit disposal.</param>
    void RegisterCollectionDraft(SparsePath path, IDisposable draft);

    /// <summary>Unregisters a collection-item draft after acceptance or explicit disposal.</summary>
    /// <param name="draft">The registered draft handle.</param>
    void UnregisterCollectionDraft(IDisposable draft);

    /// <summary>Determines whether an open draft is nested under the supplied model instance.</summary>
    /// <param name="model">The model instance that would be removed or replaced.</param>
    bool HasPendingCollectionDraftFor(object model);

    /// <summary>Determines whether an open uninserted draft is nested under the supplied path.</summary>
    /// <param name="path">The path whose subtree would be replaced or removed.</param>
    bool HasPendingCollectionDraftUnder(SparsePath path);
}
