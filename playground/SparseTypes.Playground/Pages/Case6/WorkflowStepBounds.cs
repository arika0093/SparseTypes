using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case6;

/// <summary>Render-time move boundaries for workflow steps.</summary>
/// <remarks>Each check reads one element instead of scanning for the row's index, so a full render costs O(N) comparisons. Steps are plain models, so reference identity matches the list's row identity. Click callbacks still resolve the live index by key, so a stale render-time flag can never retarget another row.</remarks>
public static class WorkflowStepBounds
{
    /// <summary>Reads whether a step is the first row of the current list order.</summary>
    /// <param name="steps">The list in render order.</param>
    /// <param name="step">The row instance.</param>
    /// <returns>True only for the first row.</returns>
    public static bool IsFirstStep(IReadOnlyList<CatalogWorkStep> steps, CatalogWorkStep step)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(step);

        return steps.Count > 0 && ReferenceEquals(steps[0], step);
    }

    /// <summary>Reads whether a step is the last row of the current list order.</summary>
    /// <param name="steps">The list in render order.</param>
    /// <param name="step">The row instance.</param>
    /// <returns>True only for the last row.</returns>
    public static bool IsLastStep(IReadOnlyList<CatalogWorkStep> steps, CatalogWorkStep step)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(step);

        return steps.Count > 0 && ReferenceEquals(steps[^1], step);
    }
}
