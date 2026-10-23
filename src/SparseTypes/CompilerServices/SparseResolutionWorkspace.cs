using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Per-build detached staging for generated conflict-resolution appliers.</summary>
/// <remarks>
/// Resolution appliers stage a touched keyed collection once per
/// <c>TryBuild</c> instead of copying it for every choice. Entries are keyed by
/// the collection's member path; a hit is only trusted when the staged buffer
/// is the slot content the applier itself published, so replaced membership
/// always re-stages. One workspace per build keeps failed choices from leaking
/// into later attempts.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseResolutionWorkspace
{
    private Dictionary<SparsePath, object?>? _staged;

    /// <summary>Builds the canonical staging key for a member path prefix.</summary>
    /// <param name="path">The resolution path being applied.</param>
    /// <param name="depth">The segment count naming the staged member.</param>
    /// <returns>A path covering exactly the member named by the prefix.</returns>
    public static SparsePath StagingKey(SparsePath path, int depth)
    {
        if (depth < 0 || depth > path.Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        return path.Prefix(depth);
    }

    /// <summary>Reads the staged buffer published for the member path.</summary>
    /// <param name="key">The staging key of the member.</param>
    /// <param name="staged">The staged buffer, or null when absent.</param>
    /// <returns><see langword="true"/> when the member was staged.</returns>
    public bool TryGetStaged(SparsePath key, out object? staged)
    {
        if (_staged is not null && _staged.TryGetValue(key, out staged))
        {
            return true;
        }

        staged = null;
        return false;
    }

    /// <summary>Publishes the staged buffer for the member path.</summary>
    /// <param name="key">The staging key of the member.</param>
    /// <param name="staged">The owned working buffer.</param>
    public void StoreStaged(SparsePath key, object staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        (_staged ??= new Dictionary<SparsePath, object?>())[key] = staged;
    }
}
