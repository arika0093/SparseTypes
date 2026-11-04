namespace SparseTypes.Tests;

/// <summary>Collection clearing while StateChanged subscribers mutate the collection.</summary>
public sealed class EditHistoryCollectionReentrancyTests
{
    [Test]
    public void ClearStillClearsNextHistoryWhenSubscriberDisposesCurrent()
    {
        var collection = new SparseEditHistoryCollection<string>();
        var first = CreateHistory();
        var second = CreateHistory();
        collection.Add(first);
        collection.Add(second);
        first.OnTransition("a");
        second.OnTransition("b");
        first.StateChanged += first.Dispose;

        collection.Clear();

        second.UndoCount.ShouldBe(0);
        second.Dispose();
    }

    [Test]
    public void ClearSkipsHistoryDisposedByEarlierSubscriber()
    {
        var collection = new SparseEditHistoryCollection<string>();
        var first = CreateHistory();
        var second = CreateHistory();
        var third = CreateHistory();
        collection.Add(first);
        collection.Add(second);
        collection.Add(third);
        first.OnTransition("a");
        second.OnTransition("b");
        third.OnTransition("c");
        first.StateChanged += second.Dispose;

        collection.Clear();

        third.UndoCount.ShouldBe(0);
        third.Dispose();
        first.Dispose();
    }

    [Test]
    public void ClearUsesSnapshotWhenSubscriberAddsHistory()
    {
        var collection = new SparseEditHistoryCollection<string>();
        var first = CreateHistory();
        var added = CreateHistory();
        collection.Add(first);
        first.OnTransition("a");
        added.OnTransition("b");
        first.StateChanged += () => collection.Add(added);

        collection.Clear();

        added.UndoCount.ShouldBe(1);
        collection.Clear();
        added.UndoCount.ShouldBe(0);
        first.Dispose();
        added.Dispose();
    }

    private static SparseEditHistory<string> CreateHistory()
    {
        return new SparseEditHistory<string>(
            new EditHistoryOptions { MaxEntries = 4 },
            new SparseEditHistoryHooks<string>
            {
                IsEmpty = static changes => changes.Length == 0,
                Invert = static changes => changes,
                Compose = static (first, second) => second,
                TryApply = (string changes, out IReadOnlyList<SparseConflict>? conflicts) =>
                {
                    conflicts = null;
                    return true;
                },
                Subscribe = static handler => { },
                Unsubscribe = static handler => { },
            }
        );
    }
}
