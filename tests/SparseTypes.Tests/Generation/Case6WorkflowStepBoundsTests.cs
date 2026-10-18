extern alias Playground;

using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case6;

namespace SparseTypes.Tests;

// Case 6 workflow rows share one render snapshot (#346): each boundary flag
// reads one element instead of scanning the step list per row per button.
// Move/remove callbacks still resolve live positions by key, so these tests
// pin both the flags and the stale-flag safety net.
public sealed class Case6WorkflowStepBoundsTests
{
    private static CatalogWorkStep Step(string id, string name = "") =>
        new()
        {
            Id = id,
            Name = name == "" ? id : name,
            ResponsibleTeam = "team",
            StandardHours = 1m,
        };

    private static CatalogWorkflow.EditSession Edit(params string[] ids)
    {
        var catalog = new EnterpriseCatalogItem
        {
            Workflow = new CatalogWorkflow { Steps = ids.Select(id => Step(id)).ToList() },
        };
        var parent = catalog.CreateEditSession();
        return parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow).Session;
    }

    private static List<CatalogWorkStep> LiveSteps(CatalogWorkflow.EditSession draft) =>
        draft.GetModelForFrameworkAccess().Steps;

    // The pre-fix WorkflowEditor.IndexOf spelling, kept as the parity oracle.
    private static int IndexOfId(IReadOnlyList<CatalogWorkStep> steps, string id)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            if (steps[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static void AssertParityWithIndexOf(IReadOnlyList<CatalogWorkStep> steps)
    {
        foreach (var step in steps)
        {
            var position = IndexOfId(steps, step.Id);
            WorkflowStepBounds
                .IsFirstStep(steps, step)
                .ShouldBe(position == 0, $"first parity for {step.Id}");
            WorkflowStepBounds
                .IsLastStep(steps, step)
                .ShouldBe(position == steps.Count - 1, $"last parity for {step.Id}");
        }
    }

    // Mirrors WorkflowEditor.MoveStep: click-time live resolution by key.
    private static void MoveStep(CatalogWorkflow.EditSession draft, string id, int delta)
    {
        var steps = LiveSteps(draft);
        var oldIndex = IndexOfId(steps, id);
        var target = oldIndex + delta;
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        if (oldIndex < 0 || target < 0 || target >= editor.Count)
        {
            return;
        }

        editor.TryMove(oldIndex, target, out _).ShouldBeTrue();
    }

    [Test, Timeout(30_000)]
    public void BoundsFlagFirstLastAndOnlyRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var empty = new List<CatalogWorkStep>();
        var ghost = Step("ghost");
        WorkflowStepBounds.IsFirstStep(empty, ghost).ShouldBeFalse();
        WorkflowStepBounds.IsLastStep(empty, ghost).ShouldBeFalse();

        var single = new List<CatalogWorkStep> { Step("a") };
        WorkflowStepBounds.IsFirstStep(single, single[0]).ShouldBeTrue();
        WorkflowStepBounds.IsLastStep(single, single[0]).ShouldBeTrue();
        WorkflowStepBounds.IsFirstStep(single, ghost).ShouldBeFalse();
        WorkflowStepBounds.IsLastStep(single, ghost).ShouldBeFalse();

        var three = new List<CatalogWorkStep> { Step("a"), Step("b"), Step("c") };
        WorkflowStepBounds.IsFirstStep(three, three[0]).ShouldBeTrue();
        WorkflowStepBounds.IsFirstStep(three, three[1]).ShouldBeFalse();
        WorkflowStepBounds.IsLastStep(three, three[2]).ShouldBeTrue();
        WorkflowStepBounds.IsLastStep(three, three[1]).ShouldBeFalse();
    }

    [Test, Timeout(30_000)]
    public void KeyedFacadeRejectsDuplicateIds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The bounds rely on unique step keys; the facade guarantees it, so
        // first/last identity checks never face an ambiguous duplicate.
        var draft = Edit("a", "b");
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        editor.TryAdd(Step("a"), out _).ShouldBeFalse();
        LiveSteps(draft).Select(static step => step.Id).ShouldBe(["a", "b"]);
    }

    [Test, Timeout(30_000)]
    public void FlagsMatchIndexOfAcrossReorderAddRemove(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var draft = Edit("a", "b", "c");
        var steps = LiveSteps(draft);
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        AssertParityWithIndexOf(steps);

        editor.TryMove(0, 2, out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["b", "c", "a"]);
        AssertParityWithIndexOf(steps);

        editor.TryAdd(Step("d"), out _).ShouldBeTrue();
        AssertParityWithIndexOf(steps);

        editor.TryRemoveByKey("b", out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["c", "a", "d"]);
        AssertParityWithIndexOf(steps);
    }

    [Test, Timeout(30_000)]
    public void FlagsMatchIndexOfAcrossUndoRedoAndKeyEdit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var draft = Edit("a", "b", "c");
        var history = draft.EnableHistory();
        var steps = LiveSteps(draft);
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );

        editor.TryMove(2, 0, out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["c", "a", "b"]);
        AssertParityWithIndexOf(steps);

        history.Undo();
        steps.Select(static step => step.Id).ShouldBe(["a", "b", "c"]);
        AssertParityWithIndexOf(steps);

        history.Redo();
        steps.Select(static step => step.Id).ShouldBe(["c", "a", "b"]);
        AssertParityWithIndexOf(steps);

        editor.TryReplaceByKey("a", Step("z", "a"), out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["c", "z", "b"]);
        AssertParityWithIndexOf(steps);
    }

    [Test, Timeout(30_000)]
    public void StaleFlagsNeverRetargetCallbacks(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var draft = Edit("a", "b", "c");
        var steps = LiveSteps(draft);
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );

        // Render captures its boundary flags, then a drag reorder lands
        // before the click: the mover's flag is now stale.
        var moverId = steps[1].Id;
        WorkflowStepBounds.IsFirstStep(steps, steps[1]).ShouldBeFalse();
        editor.TryMove(0, 2, out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["b", "c", "a"]);

        // The click still targets the live row by key, not the stale flag.
        MoveStep(draft, moverId, 1);
        steps.Select(static step => step.Id).ShouldBe(["c", "b", "a"]);

        editor.TryRemoveByKey("c", out _).ShouldBeTrue();
        steps.Select(static step => step.Id).ShouldBe(["b", "a"]);
        AssertParityWithIndexOf(steps);
    }

    [Test, Timeout(120_000)]
    public void BoundaryFlagsStayCorrectAtScale(CancellationToken cancellationToken)
    {
        foreach (var count in new[] { 10, 100, 1000 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steps = Enumerable.Range(0, count).Select(index => Step($"s{index}")).ToList();

            // One element read per flag: linear overall instead of two
            // IndexOf scans per row.
            for (var index = 0; index < steps.Count; index++)
            {
                WorkflowStepBounds.IsFirstStep(steps, steps[index]).ShouldBe(index == 0);
                WorkflowStepBounds
                    .IsLastStep(steps, steps[index])
                    .ShouldBe(index == steps.Count - 1);
            }

            AssertParityWithIndexOf(steps);
        }
    }
}
