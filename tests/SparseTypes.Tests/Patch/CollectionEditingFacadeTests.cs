namespace SparseTypes.Tests;

[SparseModel]
public partial class CollectionEditQuest
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Points { get; set; }

    public List<CollectionEditNote> Notes { get; set; } = [];
}

[SparseModel]
public partial class CollectionEditRoster
{
    public string Name { get; set; } = string.Empty;

    public List<CollectionEditQuest> Quests { get; set; } = [];

    public List<int> Scores { get; set; } = [];

    public int[] Tags { get; set; } = [];

    public List<CollectionEditQuest>? MaybeQuests { get; set; }
}

[SparseModel]
public partial class CollectionEditOrder
{
    public List<CollectionEditLine> Lines { get; set; } = [];
}

[SparseModel]
public partial class CollectionEditCustomer
{
    [SparseKey(Unassigned = "")]
    public string Id { get; set; } = string.Empty;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public List<CollectionEditQuest>? Quests { get; set; } = [];
}

[SparseModel]
public partial class CollectionEditNote
{
    [SparseKey]
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

[SparseModel]
public partial class CollectionEditNestedOrder
{
    public List<CollectionEditCustomer> Customers { get; set; } = [];

    public CollectionEditCustomer? PreferredCustomer { get; set; }
}

public partial class CollectionEditLine
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

/// <summary>Representative Case 3/4/5 editing flows through the collection facade.</summary>
public sealed class CollectionEditingFacadeTests
{
    [Test]
    public void NestedPath_ResolvesTheCurrentParentForEveryOperation()
    {
        var original = new CollectionEditCustomer { Id = "customer-1" };
        var model = new CollectionEditNestedOrder { Customers = [original] };
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditNestedOrder.SparsePath.Customers.Key("customer-1").Quests;
        var editor = session.EditCollection(collectionPath);

        editor.TryAdd(new() { Id = "first", Title = "First" }, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        original.Quests!.Select(static quest => quest.Id).ShouldBe(["first"]);

        var replacement = new CollectionEditCustomer { Id = "customer-1" };
        session.EditCollection(CollectionEditNestedOrder.SparsePath.Customers).TryReplace(0, replacement, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);

        editor.TryAdd(new() { Id = "second", Title = "Second" }, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        editor.IndexOfKey("second").ShouldBe(0);
        editor.TryGetByKey("second", out var current, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        current!.Title.ShouldBe("Second");
        original.Quests.ShouldHaveSingleItem();
        replacement.Quests!.Select(static quest => quest.Id).ShouldBe(["second"]);
    }

    [Test]
    public void NestedPath_DistinguishesUnavailableParentFromNullCollection()
    {
        var model = new CollectionEditNestedOrder { Customers = [new() { Id = "customer-1" }] };
        var session = model.CreateEditSession();
        var path = CollectionEditNestedOrder.SparsePath.Customers.Key("customer-1").Quests;
        var editor = session.EditCollection(path);

        session.EditCollection(CollectionEditNestedOrder.SparsePath.Customers).TryRemoveAt(0, out _).ShouldBeTrue();
        editor.IsAvailable(out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.PathUnavailable);

        var replacement = new CollectionEditCustomer { Id = "customer-1", Quests = null };
        session.EditCollection(CollectionEditNestedOrder.SparsePath.Customers).TryAdd(replacement, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);

        editor.IsAvailable(out error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.NullCollection);
    }

    [Test]
    public void GrandchildCollectionPath_ResolvesThroughTemporaryAndPermanentKeys()
    {
        var temporaryId = Guid.NewGuid();
        var model = new CollectionEditNestedOrder { Customers = [new() { TemporaryId = temporaryId, Quests = [new() { Id = "quest-1" }] }] };
        var session = model.CreateEditSession();
        var path = CollectionEditNestedOrder.SparsePath.Customers.TemporaryKey(temporaryId).Quests.Key("quest-1").Notes;
        var editor = session.EditCollection(path);

        editor.TryAdd(new() { Id = 1, Text = "nested" }, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Customers[0].Quests![0].Notes.Select(static note => note.Text).ShouldBe(["nested"]);
    }

    [Test]
    public void NullableParentPath_ReportsUnavailableUntilParentExists()
    {
        var model = new CollectionEditNestedOrder();
        var session = model.CreateEditSession();
        var editor = session.EditCollection(CollectionEditNestedOrder.SparsePath.PreferredCustomer.Quests);

        editor.IsAvailable(out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.PathUnavailable);

        model.PreferredCustomer = new CollectionEditCustomer();
        editor.TryAdd(new() { Id = "first" }, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.PreferredCustomer.Quests!.Select(static quest => quest.Id).ShouldBe(["first"]);
    }

    [Test]
    public void AddAndReorderFlow_GroupsOneNotificationPerAction()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.TryAdd(new() { Id = "c", Title = "Gamma" }, out var addError).ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        transitions.ShouldBe(1);

        editor.TryMoveByKey("a", 2, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        transitions.ShouldBe(2);

        model.Quests.Select(static quest => quest.Id).ShouldBe(["b", "c", "a"]);
        var changes = session.CreateChangeSet();
        changes.Quests.Added.Select(static quest => quest.Id).ShouldBe(["c"]);
        changes.Quests.OrderChanged.ShouldBeTrue();
        changes.Quests.AfterOrder.ShouldBe(["b", "c", "a"]);
    }

    [Test]
    public void AddDraftStaysOutOfParentAndBlocksAcceptanceUntilCommitted()
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        var draft = editor.BeginAdd(collectionPath.At(0), new CollectionEditQuest { Id = "new", Title = "Initial" }, session);
        draft.Session.Observable.Title = "Edited";

        model.Quests.ShouldBeEmpty();
        draft.IsOpen.ShouldBeTrue();
        session.HasPendingCollectionDrafts.ShouldBeTrue();
        session.HasPendingChildSessions.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();

        draft.TryAcceptChanges(out var error, out conflicts).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        conflicts.ShouldBeNull();
        draft.IsOpen.ShouldBeFalse();
        session.HasPendingCollectionDrafts.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
        model.Quests.Select(static quest => quest.Title).ShouldBe(["Edited"]);
    }

    [Test]
    public void CancelledAddDraftLeavesParentCollectionAndHistoryUnchanged()
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        var draft = editor.BeginAdd(collectionPath.At(0), new CollectionEditQuest { Id = "new" }, session);
        draft.Session.Observable.Title = "Draft";

        session.DiscardChildSessions();

        draft.IsOpen.ShouldBeFalse();
        session.HasPendingCollectionDrafts.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
        model.Quests.ShouldBeEmpty();
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void CollectionAndChildAcceptanceActionsAreSingleUndoSteps()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "source", Title = "Source" }] };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);

        editor.TryAdd(new CollectionEditQuest { Id = "added", Title = "Added" }, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["source"]);

        using (var duplicate = editor.BeginDuplicate("source", collectionPath.At(0), static copy => copy.Id = "copy", session))
        {
            duplicate.TryAcceptChanges(out error, out var conflicts).ShouldBeTrue();
            error.ShouldBe(SparseCollectionEditError.None);
            conflicts.ShouldBeNull();
        }

        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["source"]);

        using var child = session.OpenChildSession(collectionPath.Key("source"));
        child.Session.Observable.Title = "Edited";
        child.TryAcceptChanges(out var childConflicts).ShouldBeTrue();
        childConflicts.ShouldBeNull();
        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Quests[0].Title.ShouldBe("Source");
    }

    [Test]
    public void AddDraftDuplicateKeyFailureKeepsDraftOpenForCorrection()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "taken", Title = "Existing" }] };
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        var draft = editor.BeginAdd(collectionPath.At(0), new CollectionEditQuest { Id = "taken", Title = "Draft" }, session);

        draft.TryAcceptChanges(out var error, out var conflicts).ShouldBeFalse();

        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        conflicts.ShouldBeNull();
        draft.IsOpen.ShouldBeTrue();
        model.Quests.ShouldHaveSingleItem();
        session.HasPendingCollectionDrafts.ShouldBeTrue();

        draft.Session.Observable.Id = "new";
        draft.TryAcceptChanges(out error, out conflicts).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        conflicts.ShouldBeNull();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["taken", "new"]);
    }

    [Test]
    public void RemovedRowRestorationUsesBaselineAnchorWithoutReorderingSurvivors()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }, new() { Id = "c", Title = "Gamma" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryRemoveByKey("b", out var error).ShouldBeTrue();
        editor.TryReplaceByKey("c", new CollectionEditQuest { Id = "c", Title = "Changed" }, out error).ShouldBeTrue();
        editor.TryMoveByKey("c", 0, out error).ShouldBeTrue();
        editor.TryGetRemovedRows(out var removedRows, out error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        removedRows.ShouldHaveSingleItem();
        removedRows[0].Model.Title.ShouldBe("Beta");
        editor.TryRestoreRemoved(removedRows[0], out error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["c", "a", "b"]);
        model.Quests.Select(static quest => quest.Title).ShouldBe(["Changed", "Alpha", "Beta"]);
    }

    [Test]
    public void ResetByKeyRestoresBaselineRowWithoutChangingOrderOrOtherRows()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryReplaceByKey("a", new CollectionEditQuest { Id = "a", Title = "Changed Alpha" }, out var error).ShouldBeTrue();
        editor.TryReplaceByKey("b", new CollectionEditQuest { Id = "b", Title = "Changed Beta" }, out error).ShouldBeTrue();
        editor.TryMoveByKey("b", 0, out error).ShouldBeTrue();
        editor.TryResetByKey("a", out error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["b", "a"]);
        model.Quests.Select(static quest => quest.Title).ShouldBe(["Changed Beta", "Alpha"]);
    }

    [Test]
    public void ResetByKeyAfterDisposingAnAcceptedChildSessionRestoresTheCollectionBaseline()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var path = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(path);
        using (var child = session.OpenChildSession(path.Key("a")))
        {
            child.Session.Observable.Title = "Changed Alpha";
            child.TryAcceptChanges(out var conflicts).ShouldBeTrue();
            conflicts.ShouldBeNull();
        }

        model.Quests[0].Title.ShouldBe("Changed Alpha");
        editor.TryResetByKey("a", out var error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests[0].Title.ShouldBe("Alpha");
        using var reopened = session.OpenChildSession(path.Key("a"));
        reopened.Session.Observable.Title.ShouldBe("Alpha");
    }

    [Test]
    public void ResetByKeyRemovesRowsAddedAfterTheCurrentBaseline()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryAdd(new CollectionEditQuest { Id = "b", Title = "Added" }, out var error).ShouldBeTrue();
        editor.TryResetByKey("b", out error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["a"]);
    }

    [Test]
    public void RemovedRowHandleCannotRestoreAgainstANewerBaseline()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a" }, new() { Id = "b" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryRemoveByKey("b", out var error).ShouldBeTrue();
        editor.TryGetRemovedRows(out var rows, out error).ShouldBeTrue();
        session.AcceptChanges();

        editor.TryRestoreRemoved(rows[0], out error).ShouldBeFalse();

        error.ShouldBe(SparseCollectionEditError.StaleBaseline);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["a"]);
    }

    [Test]
    public void NestedRemovedRowCanBeRestoredFromItsRootSessionState()
    {
        var model = new CollectionEditNestedOrder
        {
            Customers = [new() { Id = "customer", Quests = [new() { Id = "a" }, new() { Id = "b", Title = "Baseline" }] }],
        };
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditNestedOrder.SparsePath.Customers.Key("customer").Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);

        editor.TryRemoveByKey("b", out var error).ShouldBeTrue();
        editor.TryGetRemovedRows(out var rows, out error).ShouldBeTrue();
        editor.TryRestoreRemoved(rows[0], out error).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        model.Customers[0].Quests!.Select(static quest => quest.Id).ShouldBe(["a", "b"]);
        model.Customers[0].Quests![1].Title.ShouldBe("Baseline");
    }

    [Test]
    public void ExistingChildDraftBlocksRemovingItsRow()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var path = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(path);
        using var child = session.OpenChildSession(path.Key("a"));
        child.Session.Observable.Title = "Draft";
        session.HasPendingChildSessionUnder(path.Key("a").UntypedPath).ShouldBeTrue();
        editor.TryGetKeyAt(0, out var key, out _).ShouldBeTrue();
        key.ShouldBe("a");
        session.HasPendingChildSessionUnder(editor.Path.Key("a")).ShouldBeTrue();

        editor.TryRemoveByKey("a", out var error).ShouldBeFalse();

        error.ShouldBe(SparseCollectionEditError.PendingChildDraft);
        model.Quests.ShouldHaveSingleItem();
    }

    [Test]
    public void RemovedTargetRetainsItsChildDraftAndBlocksRestoration()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a" }, new() { Id = "b", Title = "Baseline" }] };
        var session = model.CreateEditSession();
        var path = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(path);
        using var child = session.OpenChildSession(path.Key("b"));
        child.Session.Observable.Title = "Draft";
        model.Quests.RemoveAt(1);

        editor.TryGetRemovedRows(out var rows, out var error).ShouldBeTrue();
        editor.TryRestoreRemoved(rows[0], out error).ShouldBeFalse();

        error.ShouldBe(SparseCollectionEditError.PendingChildDraft);
        child.IsTargetMissing.ShouldBeTrue();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["a"]);
    }

    [Test]
    public void DuplicateDraftDeepClonesNestedRowsAndUsesApplicationInitialization()
    {
        var source = new CollectionEditQuest
        {
            Id = "source",
            Title = "Source",
            Notes = [new() { Id = 1, Text = "Original note" }],
        };
        var model = new CollectionEditRoster { Quests = [source] };
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        using var duplicate = editor.BeginDuplicate(
            "source",
            collectionPath.At(0),
            copy =>
            {
                copy.Id = "copy";
                copy.Notes[0].Id = 2;
            },
            session
        );
        duplicate.Session.Observable.Title = "Copy";
        duplicate.Session.Observable.Notes[0].Text = "Copied note";

        source.Title.ShouldBe("Source");
        source.Notes[0].Text.ShouldBe("Original note");
        duplicate.TryAcceptChanges(out var error, out var conflicts).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        conflicts.ShouldBeNull();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["source", "copy"]);
        model.Quests[1].Notes[0].Text.ShouldBe("Copied note");
    }

    [Test]
    public void AddDraftBlocksRemovalOrReplacementOfItsParentButAllowsUnrelatedRows()
    {
        var model = new CollectionEditNestedOrder { Customers = [new() { Id = "draft-owner" }, new() { Id = "unrelated" }] };
        var session = model.CreateEditSession();
        var draftCollectionPath = CollectionEditNestedOrder.SparsePath.Customers.Key("draft-owner").Quests;
        var draftEditor = session.EditCollection<CollectionEditQuest, string>(draftCollectionPath);
        using var draft = draftEditor.BeginAdd(draftCollectionPath.At(0), new CollectionEditQuest { Id = "quest", Title = "Draft" }, session);
        var customers = session.EditCollection<CollectionEditCustomer, string>(CollectionEditNestedOrder.SparsePath.Customers);

        customers.TryRemoveByKey("draft-owner", out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.PendingChildDraft);
        customers.TryReplaceByKey("draft-owner", new CollectionEditCustomer { Id = "draft-owner" }, out error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.PendingChildDraft);
        customers.TryRemoveByKey("unrelated", out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Customers.Select(static customer => customer.Id).ShouldBe(["draft-owner"]);
    }

    [Test]
    public void ScalarList_ReordersWithoutRemoveThenInsert()
    {
        var model = new CollectionEditRoster { Scores = [10, 20, 30] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<int>(CollectionEditRoster.SparsePath.Scores);

        editor.TryMove(0, 2, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Scores.ShouldBe([20, 30, 10]);

        editor.TryRemoveAt(0, out error).ShouldBeTrue();
        editor.TryInsert(0, 99, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Scores.ShouldBe([99, 30, 10]);
    }

    [Test]
    public void FacadeCall_JoinsOuterBatchEdit()
    {
        var model = new CollectionEditRoster { Name = "base" };
        var session = model.CreateEditSession();
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        session.BatchEdit(() =>
        {
            session.Observable.Name = "edited";
            var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);
            editor.TryAdd(new() { Id = "a", Title = "Alpha" }, out _).ShouldBeTrue();
        });

        transitions.ShouldBe(1);
        var changes = session.CreateChangeSet();
        changes.Name.After.Value.ShouldBe("edited");
        changes.Quests.Added.ShouldHaveSingleItem();
    }

    [Test]
    public void KeyedLookup_StaysStableAcrossReorder()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryGetByKey("a", out var before, out _).ShouldBeTrue();
        editor.TryMove(0, 1, out _).ShouldBeTrue();

        editor.TryGetByKey("a", out var after, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        ReferenceEquals(before, after).ShouldBeTrue();
        after!.Title.ShouldBe("Alpha");
        editor.IndexOfKey("a").ShouldBe(1);
        editor.ContainsKey("a").ShouldBeTrue();
        editor.ContainsKey("zzz").ShouldBeFalse();
    }

    [Test]
    public void RemoveByKey_ReportsRemovedInChangeSet()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryRemoveByKey("a", out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["b"]);

        var changes = session.CreateChangeSet();
        changes.Quests.Removed.Select(static quest => quest.Id).ShouldBe(["a"]);
        editor.ContainsKey("a").ShouldBeFalse();
    }

    [Test]
    public void ReplaceByKey_SameKeyEditsInPlace()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryReplaceByKey("b", new() { Id = "b", Title = "Beta v2" }, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);

        var changes = session.CreateChangeSet();
        changes.Quests.GetChange("b").IsEdited.ShouldBeTrue();
        model.Quests[0].Title.ShouldBe("Beta v2");
    }

    [Test]
    public void DuplicateAdd_FailsWithoutMutationOrNotification()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.TryAdd(new() { Id = "a", Title = "Clash" }, out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        model.Quests.Count.ShouldBe(1);
        model.Quests[0].Title.ShouldBe("Alpha");
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void KeyedLookup_ErrorsAreDeterministic()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryGetByKey("zzz", out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryRemoveByKey("zzz", out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryMoveByKey("zzz", 0, out var moveError).ShouldBeFalse();
        moveError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryReplaceByKey("zzz", new(), out var replaceError).ShouldBeFalse();
        replaceError.ShouldBe(SparseCollectionEditError.UnknownKey);
    }

    [Test]
    public void SentinelKey_NeverServesAsDurableIdentity()
    {
        var model = new CollectionEditOrder { Lines = [new() { Id = 7, Name = "saved" }, new() { Name = "legacy-pending" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditLine, int>(CollectionEditOrder.SparsePath.Lines);

        editor.IsUnassignedKey(0).ShouldBeTrue();
        editor.IsUnassignedKey(7).ShouldBeFalse();
        editor.IndexOfKey(0).ShouldBe(-1);
        editor.ContainsKey(0).ShouldBeFalse();

        editor.TryGetByKey(0, out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.UnassignedKey);
        editor.TryRemoveByKey(0, out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.UnassignedKey);

        editor.TryGetByKey(7, out var saved, out _).ShouldBeTrue();
        saved!.Name.ShouldBe("saved");
    }

    [Test]
    public void TemporaryIdentity_FlowSurvivesReorder()
    {
        var model = new CollectionEditOrder { Lines = [new() { Id = 7, Name = "saved" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditLine, int>(CollectionEditOrder.SparsePath.Lines);
        var temp = Guid.NewGuid();

        editor.TryAdd(new() { Name = "pending", TemporaryId = temp }, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out var pending, out _).ShouldBeTrue();
        pending!.Name.ShouldBe("pending");

        editor.TryMoveByKey(7, 1, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out var moved, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        ReferenceEquals(pending, moved).ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.Lines.GetTemporaryChange(temp).IsAdded.ShouldBeTrue();

        editor.TryRemoveByTemporaryKey(temp, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out _, out var missing).ShouldBeFalse();
        missing.ShouldBe(SparseCollectionEditError.UnknownTemporaryKey);
        editor.TryGetByTemporaryKey(Guid.Empty, out _, out var empty).ShouldBeFalse();
        empty.ShouldBe(SparseCollectionEditError.UnknownTemporaryKey);
    }

    [Test]
    public void ReplacedCollection_EditorKeepsWorkingOnCurrentInstance()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "old", Title = "Old" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        session.Observable.ReplaceQuests([new() { Id = "fresh", Title = "Fresh" }]);

        editor.Count.ShouldBe(1);
        editor.TryGetByKey("old", out _, out var gone).ShouldBeFalse();
        gone.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryGetByKey("fresh", out var fresh, out _).ShouldBeTrue();
        fresh!.Title.ShouldBe("Fresh");
        editor.TryAdd(new() { Id = "added", Title = "Added" }, out _).ShouldBeTrue();
        model.Quests.Count.ShouldBe(2);
    }

    [Test]
    public void UiRowKeys_ReadLivePerPosition()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(CollectionEditRoster.SparsePath.Quests);

        editor.IsKeyed.ShouldBeTrue();
        editor.KeyType.ShouldBe(typeof(string));
        editor.KeyPropertyNames.ShouldBe(["Id"]);

        var keys = new List<string?>();
        for (var index = 0; index < editor.Count; index++)
        {
            editor.TryGetKeyAt(index, out var key, out _).ShouldBeTrue();
            keys.Add(key);
        }

        keys.ShouldBe(["a", "b"]);
    }
}
