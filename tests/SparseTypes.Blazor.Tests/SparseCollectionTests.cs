using System.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseCollectionTests
{
    private static RenderFragment EditWithCollection<TModel, TPaths, TElement>(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        SparsePath path,
        Action<SparseCollection<TModel, TElement>> capture
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
                                (RenderFragment<SparseCollectionRow<TModel, TElement>>)(row => rowBuilder => rowBuilder.AddContent(0, row.Item))
                            );
                            inner.AddComponentReferenceCapture(3, component => capture((SparseCollection<TModel, TElement>)component));
                            inner.CloseComponent();
                        }
                )
            );
            builder.CloseComponent();
        };

    private sealed class ThrowingCountList<T> : IList<T>
    {
        private readonly List<T> _items = [];

        public bool ThrowOnCount { get; set; }

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public int Count => ThrowOnCount ? throw new InvalidOperationException("Collection count is unavailable.") : _items.Count;

        public bool IsReadOnly => false;

        public void Add(T item) => _items.Add(item);

        public void Clear() => _items.Clear();

        public bool Contains(T item) => _items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(T item) => _items.IndexOf(item);

        public void Insert(int index, T item) => _items.Insert(index, item);

        public bool Remove(T item) => _items.Remove(item);

        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    private sealed class TestScope(ISparseEditSession<CountedBoard> session) : ISparseEditScope<CountedBoard>
    {
        public ISparseEditSession<CountedBoard> Session { get; } = session;

        public EditContext EditContext { get; } = session.CreateEditContext();
    }

    [Test]
    public async Task CollectionCountFailureIsRenderedThroughErrorContent()
    {
        var items = new ThrowingCountList<BlazorDraftLine>();
        items.Add(new BlazorDraftLine { Id = 1, Title = "Existing" });
        var board = new CountedBoard { Drafts = items };
        var session = board.CreateEditSession();
        items.ThrowOnCount = true;
        SparseCollection<CountedBoard, BlazorDraftLine>? collection = null;
        string? renderedError = null;

        var scope = new TestScope(session);
        using var tree = await Headless.Tree.CreateAsync(builder =>
        {
            builder.OpenComponent<CascadingValue<ISparseEditScope<CountedBoard>>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<ISparseEditScope<CountedBoard>>.Value), scope);
            builder.AddAttribute(
                2,
                nameof(CascadingValue<ISparseEditScope<CountedBoard>>.ChildContent),
                (RenderFragment)(child =>
                {
                    child.OpenComponent<SparseCollection<CountedBoard, BlazorDraftLine>>(0);
                    child.AddAttribute(1, nameof(SparseCollection<CountedBoard, BlazorDraftLine>.Path), CountedBoard.SparsePath.Drafts.UntypedPath);
                    child.AddAttribute(
                        2,
                        nameof(SparseCollection<CountedBoard, BlazorDraftLine>.ErrorContent),
                        (RenderFragment<string>)(error => errorBuilder => renderedError = error)
                    );
                    child.AddComponentReferenceCapture(
                        3,
                        component => collection = (SparseCollection<CountedBoard, BlazorDraftLine>)component
                    );
                    child.CloseComponent();
                })
            );
            builder.CloseComponent();
        });

        collection.ShouldNotBeNull().Rows.ShouldBeEmpty();
        collection.QueryError!.ShouldContain("Collection count is unavailable.");
        renderedError!.ShouldContain("Collection count is unavailable.");
    }

    [Test]
    public async Task CollectionRowsKeepIdentityAcrossKeyAssignmentAndSupportEdits()
    {
        var temporaryId = Guid.NewGuid();
        var board = new BlazorDraftBoard
        {
            Drafts = [new BlazorDraftLine { Id = 2, Title = "Existing" }, new BlazorDraftLine { Id = 3, Title = "Other" }],
        };
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
        var existing = view.Rows[0];
        var other = view.Rows[1];
        existing.UiKey.ShouldNotBe(Guid.Empty);
        existing.Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(2).UntypedPath);
        existing.ItemState.ShouldNotBeNull();
        existing.ItemState!.Value.IsEmpty.ShouldBeTrue();

        view.TryAdd(
                new BlazorDraftLine
                {
                    Id = 0,
                    TemporaryId = temporaryId,
                    Title = "Draft",
                },
                out var addDraftError
            )
            .ShouldBeTrue();
        addDraftError.ShouldBe(SparseCollectionEditError.None);
        var rowsAfterAdd = view.Rows;
        await tree.FlushAsync();
        ReferenceEquals(rowsAfterAdd, view.Rows).ShouldBeTrue();

        var draft = view.Rows[2];
        draft.UiKey.ShouldNotBe(Guid.Empty);
        draft.UiKey.ShouldNotBe(existing.UiKey);
        draft.Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).UntypedPath);
        draft.Select(item => item.Title).UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).Title.UntypedPath);
        draft.ItemState.ShouldNotBeNull();
        draft.ItemState!.Value.IsAdded.ShouldBeTrue();

        draft.TryMoveTo(0, out var moveDraftError).ShouldBeTrue();
        moveDraftError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[0].UiKey.ShouldBe(draft.UiKey);
        view.Rows[0].Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).UntypedPath);
        view.Rows[0].ItemState!.Value.IsAdded.ShouldBeTrue();

        session.Observable.Drafts[0].Id = 10;
        session.Observable.Drafts[0].TemporaryId = null;
        await tree.FlushAsync();

        view.Rows[0].UiKey.ShouldBe(draft.UiKey);
        view.Rows[0].Item.Id.ShouldBe(10);
        view.Rows[0].Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(10).UntypedPath);
        view.Rows[0].ItemState.ShouldNotBeNull();
        view.Rows[0].ItemState!.Value.IsAdded.ShouldBeTrue();

        existing.TryMoveTo(2, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[2].UiKey.ShouldBe(existing.UiKey);
        view.Rows[2].Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(2).UntypedPath);
        view.Rows[2].ItemState!.Value.IsReordered.ShouldBeTrue();

        view.TryAdd(new BlazorDraftLine { Id = 4, Title = "Added" }, out var addError).ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(4);

        view.Rows[3].TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(3);
        board.Drafts.Select(static item => item.Title).ShouldBe(new[] { "Draft", "Other", "Existing" });
    }

    [Test]
    public async Task ReconcileAndApplyKeepsRowIdentityAndActionsAcrossModelReplacement()
    {
        var temporaryId = Guid.NewGuid();
        var board = new BlazorDraftBoard { Drafts = [new BlazorDraftLine { Id = 7, Title = "Existing" }] };
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
        view.TryAdd(new BlazorDraftLine { TemporaryId = temporaryId, Title = "Pending" }, out var addError).ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();

        var pendingRow = view.Rows.Single(row => row.Item.TemporaryId == temporaryId);
        var submitted = session.CreateChangeSet();
        var persisted = new BlazorDraftBoard
        {
            Drafts =
            [
                new BlazorDraftLine { Id = 7, Title = "Existing" },
                new BlazorDraftLine
                {
                    Id = 42,
                    TemporaryId = temporaryId,
                    Title = "Persisted",
                },
            ],
        };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(2);
        view.Rows[1].Item.ShouldNotBeSameAs(pendingRow.Item);
        view.Rows[1].UiKey.ShouldBe(pendingRow.UiKey);
        view.Rows[1].Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(42).UntypedPath);

        pendingRow.TryMoveTo(0, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[0].UiKey.ShouldBe(pendingRow.UiKey);

        pendingRow.TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.ShouldHaveSingleItem();
        board.Drafts.Select(static item => item.Id).ShouldBe([7]);
        view.RemovedItems.ShouldHaveSingleItem().Title.ShouldBe("Persisted");
    }

    [Test]
    public async Task SessionReplacementRefreshesRowsAndRejectsOldRowCallbacks()
    {
        var firstBoard = new BlazorDraftBoard { Drafts = [new BlazorDraftLine { Id = 7, Title = "First" }] };
        var firstSession = firstBoard.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                firstSession,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var staleRow = collection.ShouldNotBeNull().Rows[0];
        var secondBoard = new BlazorDraftBoard { Drafts = [new BlazorDraftLine { Id = 7, Title = "Second" }] };
        var secondSession = secondBoard.CreateEditSession();

        await tree.ShowAsync(
            EditWithCollection(
                secondSession,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) => collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.Rows.ShouldHaveSingleItem().Item.ShouldBeSameAs(secondBoard.Drafts[0]);
        staleRow.TryRemove(out var staleError).ShouldBeFalse();
        staleError.ShouldBe(SparseCollectionEditError.UnknownCollection);
        secondBoard.Drafts.ShouldHaveSingleItem();

        view.Rows[0].TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        secondBoard.Drafts.ShouldBeEmpty();
    }

    [Test]
    public async Task NestedCollectionPathRendersStateAndEditsTheCurrentParent()
    {
        var root = new CollectionQueryRoot { Nodes = [new CollectionQueryNode { Id = 1, Members = [new TeamMember { Id = "existing" }] }] };
        var session = root.CreateEditSession();
        SparseCollection<CollectionQueryRoot, TeamMember>? collection = null;
        var path = CollectionQueryRoot.SparsePath.Nodes.Key(1).Members;
        using var tree = await Headless.Tree.CreateAsync(builder =>
        {
            builder.OpenComponent<SparseSessionScope<CollectionQueryRoot>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<CollectionQueryRoot>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseSessionScope<CollectionQueryRoot>.ChildContent),
                EditWithCollection(session, path.UntypedPath, (SparseCollection<CollectionQueryRoot, TeamMember> captured) => collection = captured)
            );
            builder.CloseComponent();
        });

        var view = collection.ShouldNotBeNull();
        view.Rows.ShouldHaveSingleItem().Item.Id.ShouldBe("existing");
        view.Rows[0].StateQueryError.ShouldBeNull();
        view.Rows[0].ItemState.ShouldNotBeNull();
        view.Rows[0].ItemState!.Value.IsEmpty.ShouldBeTrue();

        view.TryAdd(new TeamMember { Id = "added" }, out var addError).ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();

        view.Rows.Count.ShouldBe(2);
        view.Rows.Single(row => row.Item.Id == "added").ItemState!.Value.IsAdded.ShouldBeTrue();
        root.Nodes[0].Members.Select(static item => item.Id).ShouldBe(["existing", "added"]);
    }
}
