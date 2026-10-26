namespace SparseTypes.Blazor;

/// <summary>Opens a child session using the standard <see cref="SparseConflict"/> conflict type.</summary>
/// <typeparam name="TParent">The model owned by the parent session scope.</typeparam>
/// <typeparam name="TChild">The child model addressed by <see cref="Path"/>.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
public sealed class SparseChildEditDefault<TParent, TChild, TChildPaths, TChildSession>
    : SparseChildEdit<TParent, TChild, TChildPaths, SparseConflict, TChildSession>
    where TParent : class
    where TChild : class
    where TChildPaths : ISparseTypedPath<TChild, TChild>
    where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths> { }
