using TaskObservable = global::SparseTypes.Playground.Models.PlaygroundTask.Observable;

namespace SparseTypes.Playground.Pages.Case4;

/// <summary>Render-time move boundaries captured once per task-list render.</summary>
/// <remarks>Rows share one O(1) capture instead of scanning the list per row. Click callbacks still resolve live targets by key, so a stale render-time flag can never retarget another row.</remarks>
public static class TaskBounds
{
    /// <summary>Captures the first and last rows of the current list order.</summary>
    /// <param name="tasks">The list in render order.</param>
    /// <returns>The boundary rows, or nulls when the list is empty.</returns>
    public static (TaskObservable? First, TaskObservable? Last) Capture(
        IReadOnlyList<TaskObservable> tasks
    )
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return tasks.Count == 0 ? (null, null) : (tasks[0], tasks[^1]);
    }

    /// <summary>Reads whether a row can move up from the captured boundary.</summary>
    /// <param name="first">The first row captured by <see cref="Capture" />.</param>
    /// <param name="task">The row instance.</param>
    /// <returns>False for the first row.</returns>
    public static bool CanMoveUp(TaskObservable? first, TaskObservable task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return first is not null && !ReferenceEquals(first, task);
    }

    /// <summary>Reads whether a row can move down from the captured boundary.</summary>
    /// <param name="last">The last row captured by <see cref="Capture" />.</param>
    /// <param name="task">The row instance.</param>
    /// <returns>False for the last row.</returns>
    public static bool CanMoveDown(TaskObservable? last, TaskObservable task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return last is not null && !ReferenceEquals(last, task);
    }
}
