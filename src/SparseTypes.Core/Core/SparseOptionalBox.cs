using System.ComponentModel;

namespace SparseTypes;

/// <summary>Converts typed Optional values to object-valued endpoints without losing presence.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseOptionalBox
{
    /// <summary>Boxes a present value while preserving a missing endpoint.</summary>
    /// <typeparam name="T">The endpoint value type.</typeparam>
    /// <param name="value">The typed endpoint.</param>
    /// <returns>The equivalent object-valued endpoint.</returns>
    public static Optional<object?> ToObject<T>(Optional<T> value) =>
        value.IsPresent ? Optional<object?>.Present(value.Value) : Optional<object?>.Missing;
}
