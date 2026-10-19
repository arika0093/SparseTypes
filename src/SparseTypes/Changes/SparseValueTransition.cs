using System.ComponentModel;

namespace SparseTypes;

/// <summary>A typed scalar transition shared by generated change sets.</summary>
/// <typeparam name="T">The member value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly struct SparseValueTransition<T> : ISparseValueChange<T>
{
    /// <summary>Creates a scalar transition.</summary>
    /// <param name="before">The presence-aware value before the transition.</param>
    /// <param name="after">The presence-aware value after the transition.</param>
    /// <param name="isChanged">Whether the member changed between the endpoints.</param>
    public SparseValueTransition(Optional<T> before, Optional<T> after, bool isChanged)
    {
        Before = before;
        After = after;
        IsChanged = isChanged;
    }

    /// <inheritdoc />
    public bool IsChanged { get; }

    /// <inheritdoc />
    public Optional<T> Before { get; }

    /// <inheritdoc />
    public Optional<T> After { get; }
}
