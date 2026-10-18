extern alias Playground;

using System.Reflection;
using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case4;
using TaskObservable = Playground::SparseTypes.Playground.Models.PlaygroundTask.Observable;

namespace SparseTypes.Tests;

// Case 4 rows share one parent-captured move boundary (#346) instead of one
// IndexOf scan per row per button. TaskRow callbacks still resolve live
// targets by key, so these tests pin both the boundary flags and the
// stale-flag safety net.
public sealed class Case4TaskBoundsTests
{
    private static PlaygroundSettings Tasks(params string[] ids) =>
        new()
        {
            Tasks = ids.Select(id => new PlaygroundTask
                {
                    Id = id,
                    Title = id,
                    Points = 10,
                })
                .ToList(),
        };

    private static PlaygroundSettings.EditSession Edit(params string[] ids) =>
        Tasks(ids).CreateEditSession();

    private static void AssertFlags(
        IReadOnlyList<TaskObservable> tasks,
        (TaskObservable? First, TaskObservable? Last) bounds,
        params (int Index, bool Up, bool Down)[] expected
    )
    {
        foreach (var (index, up, down) in expected)
        {
            TaskBounds.CanMoveUp(bounds.First, tasks[index]).ShouldBe(up, $"up for row {index}");
            TaskBounds
                .CanMoveDown(bounds.Last, tasks[index])
                .ShouldBe(down, $"down for row {index}");
        }
    }

    private static void AssertParityWithIndexOf(PlaygroundSettings.EditSession session)
    {
        // Re-fetched like a parent re-render: the capture always tracks the current view.
        var tasks = session.Observable.Tasks;
        var bounds = TaskBounds.Capture(tasks);
        foreach (var task in tasks)
        {
            var position = tasks.IndexOf(task);
            TaskBounds
                .CanMoveUp(bounds.First, task)
                .ShouldBe(position != 0, $"up parity for {task.Id}");
            TaskBounds
                .CanMoveDown(bounds.Last, task)
                .ShouldBe(position != tasks.Count - 1, $"down parity for {task.Id}");
        }
    }

    [Test, Timeout(30_000)]
    public void CaptureFlagsFirstLastAndOnlyRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var empty = Edit();
        var emptyBounds = TaskBounds.Capture(empty.Observable.Tasks);
        emptyBounds.First.ShouldBeNull();
        emptyBounds.Last.ShouldBeNull();

        var single = Edit("a");
        var singleTasks = single.Observable.Tasks;
        var singleBounds = TaskBounds.Capture(singleTasks);
        singleBounds.First.ShouldBeSameAs(singleTasks[0]);
        singleBounds.Last.ShouldBeSameAs(singleTasks[0]);
        AssertFlags(singleTasks, singleBounds, (0, false, false));

        var three = Edit("a", "b", "c");
        var tasks = three.Observable.Tasks;
        var bounds = TaskBounds.Capture(tasks);
        bounds.First.ShouldBeSameAs(tasks[0]);
        bounds.Last.ShouldBeSameAs(tasks[2]);
        AssertFlags(tasks, bounds, (0, false, true), (1, true, true), (2, true, false));
    }

    [Test, Timeout(30_000)]
    public void CaptureReturnsStableRowIdentities(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The boundary capture relies on cached row proxies: the same model
        // must surface the same proxy across enumeration and indexing within
        // one render, or ReferenceEquals flags would misfire.
        var session = Edit("a", "b", "c");
        var tasks = session.Observable.Tasks;
        var first = TaskBounds.Capture(tasks);
        var second = TaskBounds.Capture(tasks);
        ReferenceEquals(first.First, tasks[0]).ShouldBeTrue();
        ReferenceEquals(first.Last, tasks[2]).ShouldBeTrue();
        ReferenceEquals(second.First, first.First).ShouldBeTrue();
        ReferenceEquals(second.Last, first.Last).ShouldBeTrue();
        ReferenceEquals(tasks[1], tasks.ToList()[1]).ShouldBeTrue();
    }

    [Test, Timeout(30_000)]
    public void FlagsMatchIndexOfAcrossReorderAddRemove(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = Edit("a", "b", "c");
        AssertParityWithIndexOf(session);

        session.Observable.Tasks.Move(0, 2);
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["b", "c", "a"]);
        AssertParityWithIndexOf(session);

        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        editor
            .TryAdd(
                new PlaygroundTask
                {
                    Id = "d",
                    Title = "d",
                    Points = 1,
                },
                out _
            )
            .ShouldBeTrue();
        AssertParityWithIndexOf(session);

        editor.TryRemoveByKey("b", out _).ShouldBeTrue();
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["c", "a", "d"]);
        AssertParityWithIndexOf(session);
    }

    [Test, Timeout(30_000)]
    public void FlagsMatchIndexOfAcrossUndoRedoAndKeyEdit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = Edit("a", "b", "c");
        using var history = session.CreateHistory();

        session.Observable.Tasks.Move(2, 0);
        AssertParityWithIndexOf(session);

        history.Undo();
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["a", "b", "c"]);
        AssertParityWithIndexOf(session);

        history.Redo();
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["c", "a", "b"]);
        AssertParityWithIndexOf(session);

        // A key edit replaces the row instance: the fresh capture tracks the
        // new proxy while the old proxy no longer resolves.
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        var stale = session.Observable.Tasks.First(static task => task.Id == "a");
        editor
            .TryReplaceByKey(
                "a",
                new PlaygroundTask
                {
                    Id = "z",
                    Title = "a",
                    Points = 10,
                },
                out _
            )
            .ShouldBeTrue();
        var tasks = session.Observable.Tasks;
        tasks.Select(static task => task.Id).ShouldBe(["c", "z", "b"]);
        tasks.IndexOf(stale).ShouldBe(-1);
        AssertParityWithIndexOf(session);
    }

    [Test, Timeout(30_000)]
    public void StaleFlagsNeverRetargetCallbacks(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = Edit("a", "b", "c");
        var tasks = session.Observable.Tasks;
        var staleBounds = TaskBounds.Capture(tasks);
        var staleMover = tasks[1];

        // Mirror TaskListEditor.OnSortableUpdate: drag the head to the end
        // after the render captured its boundaries.
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        editor.TryMove(0, 2, out _).ShouldBeTrue();
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["b", "c", "a"]);

        // Mirror TaskRow.MoveTask: resolve the live index by key instead of
        // trusting the render-time flag, so the stale row still moves itself.
        var live = editor.IndexOfKey(staleMover.Id);
        live.ShouldBe(0);
        editor.TryMoveByKey(staleMover.Id, live + 1, out _).ShouldBeTrue();
        session.Observable.Tasks.Select(static task => task.Id).ShouldBe(["c", "b", "a"]);

        // Mirror TaskRow.RemoveTask: keyed removal targets the row even when
        // its render-time position is stale.
        editor.TryRemoveByKey("c", out _).ShouldBeTrue();
        var freshTasks = session.Observable.Tasks;
        freshTasks.Select(static task => task.Id).ShouldBe(["b", "a"]);

        var fresh = TaskBounds.Capture(freshTasks);
        ReferenceEquals(fresh.First, staleBounds.First!).ShouldBeFalse();
        TaskBounds.CanMoveUp(fresh.First, staleBounds.First!).ShouldBeTrue();
        AssertFlags(freshTasks, fresh, (0, false, true), (1, true, false));
    }

    [Test, Timeout(30_000)]
    public void RowsReceiveParentFlagsNotPerRowScans(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playground = typeof(TaskBounds).Assembly;
        var row = playground.GetType("SparseTypes.Playground.Pages.Case4.TaskRow");
        row.ShouldNotBeNull();

        // The render-time move state arrives from the parent capture.
        row!.GetProperty("CanMoveUp").ShouldNotBeNull();
        row.GetProperty("CanMoveDown").ShouldNotBeNull();
        // No per-row position scan remains on the row.
        row.GetProperty(
                "Index",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
            )
            .ShouldBeNull();
    }

    [Test, Timeout(120_000)]
    public void BoundaryFlagsStayCorrectAtScale(CancellationToken cancellationToken)
    {
        foreach (var count in new[] { 10, 100, 1000 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = Edit(Enumerable.Range(0, count).Select(index => $"t{index}").ToArray());
            var tasks = session.Observable.Tasks;
            var bounds = TaskBounds.Capture(tasks);

            // One capture plus one O(1) flag per row: linear overall instead
            // of two IndexOf scans per row.
            for (var index = 0; index < tasks.Count; index++)
            {
                TaskBounds.CanMoveUp(bounds.First, tasks[index]).ShouldBe(index != 0);
                TaskBounds
                    .CanMoveDown(bounds.Last, tasks[index])
                    .ShouldBe(index != tasks.Count - 1);
            }

            AssertParityWithIndexOf(session);
        }
    }
}
