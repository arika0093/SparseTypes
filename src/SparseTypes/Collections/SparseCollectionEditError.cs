using System.ComponentModel;

namespace SparseTypes;

/// <summary>Deterministic failure reasons for path-addressed collection edits.</summary>
/// <remarks>Every <c>Try*</c> member of the collection editing facade reports one of these values instead of a bare boolean.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseCollectionEditError
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The addressed member name is not a known member of the session model.</summary>
    UnknownCollection = 1,

    /// <summary>The addressed member exists but is not a sequence collection.</summary>
    NotACollection = 2,

    /// <summary>The addressed sequence member currently holds no collection instance.</summary>
    NullCollection = 3,

    /// <summary>The backing collection cannot be resized through the observable pipeline.</summary>
    NotMutable = 4,

    /// <summary>A positional index lies outside the current collection bounds.</summary>
    IndexOutOfRange = 5,

    /// <summary>An addition or replacement would duplicate an assigned key, temporary identity, or null slot.</summary>
    DuplicateKey = 6,

    /// <summary>The requested assigned key does not resolve to one current item.</summary>
    UnknownKey = 7,

    /// <summary>An unassigned sentinel or missing temporary identity was used as durable identity.</summary>
    UnassignedKey = 8,

    /// <summary>The requested temporary identity does not resolve to one current item.</summary>
    UnknownTemporaryKey = 9,

    /// <summary>The supplied item cannot be converted to the collection element type.</summary>
    InvalidItem = 10,

    /// <summary>A nested collection path cannot resolve a parent, keyed item, or member.</summary>
    PathUnavailable = 11,

    /// <summary>A nested child edit cannot be accepted because it has conflicts or an uncommitted draft.</summary>
    ChildSessionConflict = 12,

    /// <summary>A nested uninserted collection-item draft must be accepted or discarded first.</summary>
    PendingChildDraft = 13,

    /// <summary>The operation requires a generated baseline-aware collection editor.</summary>
    BaselineUnavailable = 14,

    /// <summary>The removed-row handle belongs to an older retained baseline.</summary>
    StaleBaseline = 15,
}
