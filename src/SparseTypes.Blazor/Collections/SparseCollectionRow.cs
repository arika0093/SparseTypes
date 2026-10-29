using System.Linq.Expressions;

namespace SparseTypes.Blazor;

/// <summary>A stable-identity row supplied to <see cref="SparseCollection{TModel, TElement}"/>.</summary>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TElement">The collection element model type.</typeparam>
public sealed class SparseCollectionRow<TModel, TElement>
    where TModel : class
    where TElement : class
{
    internal delegate bool RemoveOperation(SparseCollectionRow<TModel, TElement> row, out SparseCollectionEditError error);
    internal delegate bool MoveOperation(SparseCollectionRow<TModel, TElement> row, int newIndex, out SparseCollectionEditError error);

    private readonly RemoveOperation _remove;
    private readonly MoveOperation _move;

    internal SparseCollectionRow(
        TElement item,
        int index,
        Guid uiKey,
        SparsePath<TModel, TElement> path,
        SparseCollectionItemState? itemState,
        string? stateQueryError,
        IReadOnlyList<string> validationMessages,
        RemoveOperation remove,
        MoveOperation move
    )
    {
        Item = item;
        Index = index;
        UiKey = uiKey;
        Path = path;
        ItemState = itemState;
        StateQueryError = stateQueryError;
        ValidationMessages = validationMessages;
        _remove = remove;
        _move = move;
    }

    /// <summary>Gets the live collection element model.</summary>
    public TElement Item { get; }

    /// <summary>Gets the row's current index when the collection was rendered.</summary>
    public int Index { get; }

    /// <summary>Gets the UI identity, which remains stable for a keyed row across model replacement.</summary>
    public Guid UiKey { get; }

    /// <summary>Gets the typed, root-preserving path to this row.</summary>
    public SparsePath<TModel, TElement> Path { get; }

    /// <summary>Gets the row's semantic state snapshot, when the collection supports item state.</summary>
    /// <remarks>Item state is available for keyed collections; unkeyed collections expose positional paths only.</remarks>
    public SparseCollectionItemState? ItemState { get; }

    /// <summary>Gets why the item state snapshot was unavailable, or null when it was available.</summary>
    public string? StateQueryError { get; }

    /// <summary>Gets controller validation messages at this row or any nested path.</summary>
    public IReadOnlyList<string> ValidationMessages { get; }

    /// <summary>Gets the item state snapshot or throws when the collection cannot provide one.</summary>
    /// <exception cref="InvalidOperationException">The item state query was unavailable.</exception>
    public SparseCollectionItemState RequireItemState() =>
        ItemState ?? throw new InvalidOperationException(StateQueryError ?? "The collection item state is unavailable.");

    /// <summary>Navigates from this row to a child member with a root-preserving typed path.</summary>
    /// <param name="selector">A member-access chain from the row model.</param>
    /// <typeparam name="TValue">The addressed member type.</typeparam>
    /// <returns>A typed path usable by <see cref="SparseField{TModel, TValue}"/>.</returns>
    public SparsePath<TModel, TValue> Select<TValue>(Expression<Func<TElement, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new SparsePath<TModel, TValue>(
            SparseFieldContext<TModel, object?>.AppendSelector(Path.Path, typeof(TElement), selector, nameof(selector))
        );
    }

    /// <summary>Removes this row if the same model instance is still present.</summary>
    public bool TryRemove(out SparseCollectionEditError error) => _remove(this, out error);

    /// <summary>Moves this row to the requested index if it is still present.</summary>
    /// <param name="newIndex">The target index.</param>
    /// <param name="error">The failure reason, or <see cref="SparseCollectionEditError.None"/> on success.</param>
    /// <returns>True when the row moved.</returns>
    public bool TryMoveTo(int newIndex, out SparseCollectionEditError error) => _move(this, newIndex, out error);
}
