using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Regression coverage for #343 (one shared remove/move delegate pair per
// rebuild) and #344 (one cached removed-item projection per snapshot).
public sealed class SparseCollectionAllocationTests
{
    private static RenderFragment EditWithCollection<TModel, TPaths, TElement>(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        SparsePath path,
        Action<SparseCollection<TModel, TElement>> capture,
        RenderFragment<SparseCollectionRow<TModel, TElement>>? rowTemplate = null
    )
        where TModel : class
        where TPaths : ISparseTypedPath<TModel, TModel>
        where TElement : class =>
        builder =>
        {
            builder.OpenComponent<SparseEdit<TModel, TPaths>>(0);
            builder.AddAttribute(1, nameof(SparseEdit<TModel, TPaths>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseEdit<TModel, TPaths>.ChildContent),
                (RenderFragment<SparseEditContext<TModel, TPaths>>)(
                    _ =>
                        inner =>
                        {
                            inner.OpenComponent<SparseCollection<TModel, TElement>>(0);
                            inner.AddAttribute(1, nameof(SparseCollection<TModel, TElement>.Path), path);
                            inner.AddAttribute(
                                2,
                                nameof(SparseCollection<TModel, TElement>.RowTemplate),
                                rowTemplate
                                    ?? (RenderFragment<SparseCollectionRow<TModel, TElement>>)(
                                        row => rowBuilder => rowBuilder.AddContent(0, row.Item)
                                    )
                            );
                            inner.AddComponentReferenceCapture(3, component => capture((SparseCollection<TModel, TElement>)component));
                            inner.CloseComponent();
                        }
                )
            );
            builder.CloseComponent();
        };

    private static object RowDelegate(SparseCollectionRow<BlazorDraftBoard, BlazorDraftLine> row, string field) =>
        typeof(SparseCollectionRow<BlazorDraftBoard, BlazorDraftLine>)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldNotBeNull()
            .GetValue(row)
            .ShouldNotBeNull();

    private static BlazorDraftBoard BoardWith(params (int Id, string Title)[] drafts) =>
        new() { Drafts = drafts.Select(static draft => new BlazorDraftLine { Id = draft.Id, Title = draft.Title }).ToList() };

    [Test]
    public async Task RowsInOneRebuildShareBothOperationDelegates()
    {
        var session = BoardWith((1, "One"), (2, "Two"), (3, "Three")).CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.Rows.Count.ShouldBe(3);
        var remove = RowDelegate(view.Rows[0], "_remove");
        var move = RowDelegate(view.Rows[0], "_move");
        foreach (var row in view.Rows.Skip(1))
        {
            RowDelegate(row, "_remove").ShouldBeSameAs(remove);
            RowDelegate(row, "_move").ShouldBeSameAs(move);
        }
    }

    [Test]
    public async Task RebuildMintsFreshDelegatesAndRetiredRowsStayRejected()
    {
        var session = BoardWith((1, "One"), (2, "Two")).CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        var staleRow = view.Rows[0];
        var staleRemove = RowDelegate(staleRow, "_remove");
        var staleMove = RowDelegate(staleRow, "_move");

        view.TryAdd(new BlazorDraftLine { Id = 3, Title = "Three" }, out _).ShouldBeTrue();
        await tree.FlushAsync();

        view.Rows.Count.ShouldBe(3);
        var remove = RowDelegate(view.Rows[0], "_remove");
        var move = RowDelegate(view.Rows[0], "_move");
        remove.ShouldNotBeSameAs(staleRemove);
        move.ShouldNotBeSameAs(staleMove);
        foreach (var row in view.Rows.Skip(1))
        {
            RowDelegate(row, "_remove").ShouldBeSameAs(remove);
            RowDelegate(row, "_move").ShouldBeSameAs(move);
        }

        var secondBoard = BoardWith((7, "Second"));
        await tree.ShowAsync(
            EditWithCollection(
                secondBoard.CreateEditSession(),
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        staleRow.TryRemove(out var staleRemoveError).ShouldBeFalse();
        staleRemoveError.ShouldBe(SparseCollectionEditError.UnknownCollection);
        staleRow.TryMoveTo(0, out var staleMoveError).ShouldBeFalse();
        staleMoveError.ShouldBe(SparseCollectionEditError.UnknownCollection);
        secondBoard.Drafts.Count.ShouldBe(1);
    }

    [Test]
    public async Task SharedDelegatesStillTargetTheCorrectRowAfterReorder()
    {
        var board = BoardWith((1, "One"), (2, "Two"), (3, "Three"));
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                board.CreateEditSession(),
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        var last = view.Rows[2];
        last.TryMoveTo(0, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[0].UiKey.ShouldBe(last.UiKey);

        view.Rows[1].TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        board.Drafts.Select(static item => item.Title).ShouldBe(["Three", "Two"]);
    }

    [Test]
    public async Task EmptyCollectionBuildsNoDelegatesAndReadsEmptyRemovals()
    {
        var session = new BlazorDraftBoard().CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.Rows.ShouldBeEmpty();
        view.RemovedItems.ShouldBeEmpty();
        view.RemovedItems.ShouldBeSameAs(view.RemovedItems);
    }

    [Test]
    public async Task RemovedItemsReadsShareOneReadOnlyProjection()
    {
        var board = BoardWith((1, "One"), (2, "Two"), (3, "Three"));
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                board.CreateEditSession(),
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.Rows[0].TryRemove(out _).ShouldBeTrue();
        view.Rows[1].TryRemove(out _).ShouldBeTrue();
        await tree.FlushAsync();

        var first = view.RemovedItems;
        first.Select(static item => item.Title).ShouldBe(["One", "Three"]);
        view.RemovedItems.ShouldBeSameAs(first);

        ((System.Collections.Generic.ICollection<BlazorDraftLine>)first).IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => ((System.Collections.Generic.IList<BlazorDraftLine>)first).Add(new BlazorDraftLine()));
        view.RemovedItems.ShouldBeSameAs(first);
        first.Select(static item => item.Title).ShouldBe(["One", "Three"]);
    }

    [Test]
    public async Task RemovedItemsEmptyReadsAllocateNothing()
    {
        var board = BoardWith((1, "One"), (2, "Two"));
        var session = board.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.RemovedItems.ShouldBeEmpty();

        // An edit-only change keeps the snapshot removal-free.
        session.Observable.Drafts[0].Title = "One (edited)";
        await tree.FlushAsync();
        var first = view.RemovedItems;
        first.ShouldBeEmpty();
        first.ShouldBeSameAs(Array.Empty<BlazorDraftLine>());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            _ = view.RemovedItems;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine($"removed-empty-reads: bytes={allocated} for 1000 reads");
        allocated.ShouldBe(0);
        view.RemovedItems.ShouldBeSameAs(first);
    }

    [Test]
    public async Task RemovedItemsInvalidatedByRefreshAndRebind()
    {
        var board = BoardWith((1, "One"), (2, "Two"));
        var session = board.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        var beforeRemoval = view.RemovedItems;
        beforeRemoval.ShouldBeEmpty();

        view.Rows[0].TryRemove(out _).ShouldBeTrue();
        await tree.FlushAsync();
        var afterRemoval = view.RemovedItems;
        afterRemoval.ShouldNotBeSameAs(beforeRemoval);
        afterRemoval.ShouldHaveSingleItem().Title.ShouldBe("One");
        view.RemovedItems.ShouldBeSameAs(afterRemoval);

        // Restoring the removed key recomputes rather than mutating the cache.
        view.TryAdd(new BlazorDraftLine { Id = 1, Title = "One" }, out _).ShouldBeTrue();
        await tree.FlushAsync();
        var afterRestore = view.RemovedItems;
        afterRestore.ShouldBeEmpty();
        afterRestore.ShouldNotBeSameAs(afterRemoval);

        await tree.ShowAsync(
            EditWithCollection(
                BoardWith((9, "Fresh")).CreateEditSession(),
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );
        collection.ShouldNotBeNull().RemovedItems.ShouldBeEmpty();
    }

    [Test]
    public async Task MalformedRemovedEntryThrowsWithoutPoisoningTheCache()
    {
        var board = BoardWith((1, "One"));
        var session = board.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.RemovedItems.ShouldBeEmpty();

        // Simulate a snapshot replacement whose removed entry carries no
        // baseline value, clearing the cache exactly as SetCollectionState does.
        var stateType = typeof(SparseCollectionState);
        var constructor = stateType
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 9);
        object badEntry = default(SparseCollectionItemState);
        stateType
            .Assembly.GetType("SparseTypes.SparseCollectionItemState")
            .ShouldNotBeNull()
            .GetProperty("IsRemoved")
            .ShouldNotBeNull()
            .SetValue(badEntry, true);
        var badState = (SparseCollectionState)
            constructor.Invoke([
                BlazorDraftBoard.SparsePath.Drafts.UntypedPath,
                false,
                true,
                false,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                new List<SparseCollectionItemState> { (SparseCollectionItemState)badEntry },
                (Func<object?, SparseCollectionItemState>)(static _ => default),
                (Func<Guid, SparseCollectionItemState>)(static _ => default),
            ]);
        var viewType = typeof(SparseCollection<BlazorDraftBoard, BlazorDraftLine>);
        viewType.GetField("_collectionState", BindingFlags.Instance | BindingFlags.NonPublic).ShouldNotBeNull().SetValue(view, badState);
        var removedField = viewType.GetField("_removedItems", BindingFlags.Instance | BindingFlags.NonPublic).ShouldNotBeNull();
        removedField.SetValue(view, null);

        Should.Throw<InvalidOperationException>(() => view.RemovedItems);
        removedField.GetValue(view).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => view.RemovedItems);

        // The next genuine refresh replaces the poisoned snapshot.
        session.Observable.Drafts[0].Title = "One (edited)";
        await tree.FlushAsync();
        view.RemovedItems.ShouldBeEmpty();
    }

    [Test]
    public async Task RenderReadsRemovedItemsCase6Style()
    {
        var board = BoardWith((1, "One"), (2, "Two"));
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? seen = null;
        var observedCounts = new List<int>();

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                board.CreateEditSession(),
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => seen = captured,
                row =>
                    rowBuilder =>
                    {
                        // Mirrors the Case6 editors' `?.RemovedItems ?? []` render reads.
                        observedCounts.Add((seen?.RemovedItems ?? []).Count);
                        rowBuilder.AddContent(0, row.Item.Title);
                    }
            )
        );

        var view = seen.ShouldNotBeNull();
        await tree.FlushAsync();
        observedCounts.ShouldNotBeEmpty();
        observedCounts[^1].ShouldBe(0);

        view.Rows[0].TryRemove(out _).ShouldBeTrue();
        await tree.FlushAsync();
        observedCounts[^1].ShouldBe(1);
        view.RemovedItems.ShouldHaveSingleItem().Title.ShouldBe("One");
    }

    [Test]
    public async Task RebuildAllocationSmoke()
    {
        foreach (var rowCount in new[] { 0, 1, 100 })
        {
            var drafts = Enumerable.Range(0, rowCount).Select(static index => (index + 1, $"Draft {index + 1}")).ToArray();
            SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

            using var tree = await Headless.Tree.CreateAsync(
                EditWithCollection(
                    BoardWith(drafts).CreateEditSession(),
                    BlazorDraftBoard.SparsePath.Drafts,
                    (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
                )
            );

            var view = collection.ShouldNotBeNull();
            var rebuild = typeof(SparseCollection<BlazorDraftBoard, BlazorDraftLine>)
                .GetMethod("RebuildRows", BindingFlags.Instance | BindingFlags.NonPublic)
                .ShouldNotBeNull();
            view.Rows.Count.ShouldBe(rowCount);

            const int rebuilds = 50;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < rebuilds; index++)
            {
                rebuild.Invoke(view, null);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine($"rebuild-smoke rows={rowCount}: rebuilds={rebuilds} allocated-bytes={allocated}");
            view.Rows.Count.ShouldBe(rowCount);
            if (rowCount > 1)
            {
                var remove = RowDelegate(view.Rows[0], "_remove");
                foreach (var row in view.Rows.Skip(1))
                {
                    RowDelegate(row, "_remove").ShouldBeSameAs(remove);
                }
            }
        }
    }
}
