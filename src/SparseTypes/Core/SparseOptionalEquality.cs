using System;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Presence-aware equality helpers used by generated fragments.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseOptionalEquality
{
    /// <summary>Compares optional presence and delegates present-value equality.</summary>
    /// <typeparam name="T">The optional value type.</typeparam>
    /// <param name="left">The first optional value.</param>
    /// <param name="right">The second optional value.</param>
    /// <param name="valueEquals">Compares the present optional values.</param>
    /// <returns>True when presence and the present values are equal.</returns>
    public static bool AreEqual<T>(
        Optional<T> left,
        Optional<T> right,
        Func<Optional<T>, Optional<T>, bool> valueEquals
    )
    {
        ArgumentNullException.ThrowIfNull(valueEquals);
        if (!left.IsPresent)
        {
            return !right.IsPresent;
        }

        return right.IsPresent && valueEquals(left, right);
    }
}
