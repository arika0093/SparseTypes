namespace SparseTypes.Tests;

/// <summary>Undo/redo replay while the apply callback clears or disposes the history.</summary>
public sealed class EditHistoryReplayReentrancyTests
{
    [Test]
    public void UndoSucceedsWhenApplyClearsHistory()
    {
        SparseEditHistory<string>? history = null;
        history = CreateHistory(() => history!.Clear());
        history.OnTransition("a");

        history.TryUndo(out _).ShouldBeTrue();

        history.UndoCount.ShouldBe(0);
        history.RedoCount.ShouldBe(0);
        history.Dispose();
    }

    [Test]
    public void RedoSucceedsWhenApplyClearsHistory()
    {
        var clear = false;
        SparseEditHistory<string>? history = null;
        history = CreateHistory(() =>
        {
            if (clear)
            {
                history!.Clear();
            }
        });
        history.OnTransition("a");
        history.Undo();
        clear = true;

        history.TryRedo(out _).ShouldBeTrue();

        history.UndoCount.ShouldBe(0);
        history.RedoCount.ShouldBe(0);
        history.Dispose();
    }

    [Test]
    public void UndoSucceedsWhenApplyDisposesHistory()
    {
        SparseEditHistory<string>? history = null;
        history = CreateHistory(() => history!.Dispose());
        history.OnTransition("a");

        history.TryUndo(out _).ShouldBeTrue();

        Should.Throw<ObjectDisposedException>(() => _ = history.UndoCount);
    }

    [Test]
    public void RedoSucceedsWhenApplyDisposesHistory()
    {
        var dispose = false;
        SparseEditHistory<string>? history = null;
        history = CreateHistory(() =>
        {
            if (dispose)
            {
                history!.Dispose();
            }
        });
        history.OnTransition("a");
        history.Undo();
        dispose = true;

        history.TryRedo(out _).ShouldBeTrue();

        Should.Throw<ObjectDisposedException>(() => _ = history.RedoCount);
    }

    private static SparseEditHistory<string> CreateHistory(Action onApply)
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
                    onApply();
                    return true;
                },
                Subscribe = static handler => { },
                Unsubscribe = static handler => { },
            }
        );
    }
}
