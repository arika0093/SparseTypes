namespace SparseTypes.Tests;

/// <summary>Discarding an add draft must not validate the abandoned model.</summary>
public sealed class CollectionAddDraftDiscardTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void DiscardingDraftWithDuplicateChildKeysClosesWithoutMutatingParent(bool dispose)
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        var draft = editor.BeginAdd(collectionPath.At(0), new CollectionEditQuest { Id = "new" }, session);
        draft.Session.Observable.Notes.Model.Add(new CollectionEditNote { Id = 1, Text = "a" });
        draft.Session.Observable.Notes.Model.Add(new CollectionEditNote { Id = 1, Text = "b" });

        if (dispose)
        {
            draft.Dispose();
        }
        else
        {
            draft.DiscardChanges();
        }

        draft.IsOpen.ShouldBeFalse();
        session.HasPendingCollectionDrafts.ShouldBeFalse();
        model.Quests.ShouldBeEmpty();
        session.TryAcceptChildSessions(out _).ShouldBeTrue();
    }

    [Test]
    public void DiscardingDraftAlsoDiscardsNestedChildDrafts()
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();
        var collectionPath = CollectionEditRoster.SparsePath.Quests;
        var editor = session.EditCollection<CollectionEditQuest, string>(collectionPath);
        var draft = editor.BeginAdd(collectionPath.At(0), new CollectionEditQuest { Id = "new" }, session);
        var notesPath = CollectionEditQuest.SparsePath.Notes;
        var noteEditor = draft.Session.EditCollection<CollectionEditNote, int>(notesPath);
        var note = noteEditor.BeginAdd(notesPath.At(0), new CollectionEditNote { Id = 1 }, draft.Session);
        draft.Session.Observable.Notes.Model.Add(new CollectionEditNote { Id = 2 });
        draft.Session.Observable.Notes.Model.Add(new CollectionEditNote { Id = 2 });

        draft.DiscardChanges();

        note.IsOpen.ShouldBeFalse();
        draft.IsOpen.ShouldBeFalse();
        session.HasPendingCollectionDrafts.ShouldBeFalse();
        model.Quests.ShouldBeEmpty();
    }
}
