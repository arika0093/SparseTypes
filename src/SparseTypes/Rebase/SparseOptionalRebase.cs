using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Reconciles presence-aware scalar endpoints for generated rebases.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseOptionalRebase
{
    /// <summary>Resolves the default three-way scalar rebase rules.</summary>
    /// <typeparam name="T">The member value type.</typeparam>
    /// <typeparam name="TMode">The product's rebase mode enum.</typeparam>
    /// <param name="before">The baseline endpoint.</param>
    /// <param name="desired">The locally desired endpoint.</param>
    /// <param name="current">The concurrently observed endpoint.</param>
    /// <param name="mode">The requested fallback mode.</param>
    /// <param name="preferIncoming">The mode that selects the desired value on divergence.</param>
    /// <param name="preferCurrent">The mode that keeps the concurrent value on divergence.</param>
    /// <param name="areEqual">Presence-aware member equality.</param>
    /// <param name="after">The desired endpoint when a transition is selected.</param>
    /// <param name="hasConflict">Whether the endpoints conflict under the selected mode.</param>
    /// <returns>True when a transition should be emitted.</returns>
    public static bool TryResolve<T, TMode>(
        Optional<T> before,
        Optional<T> desired,
        Optional<T> current,
        TMode mode,
        TMode preferIncoming,
        TMode preferCurrent,
        System.Func<Optional<T>, Optional<T>, bool> areEqual,
        out Optional<T> after,
        out bool hasConflict
    )
        where TMode : struct
    {
        if (areEqual == null)
        {
            throw new System.ArgumentNullException(nameof(areEqual));
        }

        var modeComparer = EqualityComparer<TMode>.Default;
        if (modeComparer.Equals(mode, preferIncoming) && !areEqual(desired, current))
        {
            after = desired;
            hasConflict = false;
            return true;
        }

        if (!modeComparer.Equals(mode, preferCurrent) && areEqual(before, current))
        {
            after = desired;
            hasConflict = false;
            return true;
        }

        hasConflict = !modeComparer.Equals(mode, preferCurrent) && !areEqual(desired, current);
        after = default;
        return false;
    }
}
