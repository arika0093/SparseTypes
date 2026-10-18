extern alias Playground;

using Playground::SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Patch;

// Case 6 WorkflowEditor history wiring (#345): a successful child Undo/Redo
// must reach the workflow parent exactly once, after the sortable-list
// version advances. The harness below mirrors the production call graph:
// ChildSessionActions fires both events per successful history action, and
// the fixed WorkflowEditor subscribes only the versioned OnChanged route.
// Status-only edits still report through the CatalogDraftStatus route.
public sealed class Case6WorkflowHistoryNotificationTests
{
    // Mirrors WorkflowEditor's handler set post-fix.
    private sealed class WorkflowNotifications
    {
        public int SortableListsVersion { get; private set; }

        public int ParentCallbacks { get; private set; }

        public List<int> VersionsSeenByParent { get; } = [];

        public Task OnHistoryChanged() => NotifyWorkflowChanged();

        public Task RefreshWorkflow()
        {
            SortableListsVersion++;
            return OnParentChanged();
        }

        // Status-only route kept for CatalogDraftStatus; never bound to the
        // history buttons post-fix, so it must not advance the version.
        public Task ReportStatus(bool _) => OnParentChanged();

        private Task NotifyWorkflowChanged()
        {
            SortableListsVersion++;
            return OnParentChanged();
        }

        private Task OnParentChanged()
        {
            ParentCallbacks++;
            VersionsSeenByParent.Add(SortableListsVersion);
            return Task.CompletedTask;
        }
    }

    private static (
        EnterpriseCatalogItem Live,
        EnterpriseCatalogItem.EditSession Parent
    ) WorkflowParent(params string[] ids)
    {
        var live = new EnterpriseCatalogItem
        {
            Workflow = new CatalogWorkflow
            {
                Steps = ids.Select(id => new CatalogWorkStep
                    {
                        Id = id,
                        Name = id,
                        ResponsibleTeam = "team",
                        StandardHours = 1m,
                    })
                    .ToList(),
            },
        };
        return (live, live.CreateEditSession());
    }

    private static List<string> LiveIds(CatalogWorkflow.EditSession draft) =>
        draft.GetModelForFrameworkAccess().Steps.Select(static step => step.Id).ToList();

    // Mirrors ChildSessionActions.UndoDraft/RedoDraft: the history operation
    // runs first and throws InvalidOperationException on failure (caught by
    // the component, notifying nothing); on success both events fire, and
    // the fixed WorkflowEditor receives only the versioned one.
    private static async Task EmitHistorySucceeded(
        Action historyAction,
        WorkflowNotifications notifications
    )
    {
        historyAction();
        await notifications.OnHistoryChanged();
    }

    [Test, Timeout(30_000)]
    public async Task UndoNotifiesParentOnceWithAdvancedVersion(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, parent) = WorkflowParent("a", "b", "c");
        var child = parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow);
        var draft = child.Session;
        var history = draft.EnableHistory();
        var notifications = new WorkflowNotifications();
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        editor.TryMove(0, 2, out _).ShouldBeTrue();
        LiveIds(draft).ShouldBe(["b", "c", "a"]);
        child.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();

        await EmitHistorySucceeded(history.Undo, notifications);

        notifications.ParentCallbacks.ShouldBe(1);
        notifications.SortableListsVersion.ShouldBe(1);
        notifications.VersionsSeenByParent.ShouldBe([1]);
        LiveIds(draft).ShouldBe(["a", "b", "c"]);
        child.HasChanges.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeTrue();
    }

    [Test, Timeout(30_000)]
    public async Task RedoNotifiesParentOnceWithAdvancedVersion(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, parent) = WorkflowParent("a", "b", "c");
        var child = parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow);
        var draft = child.Session;
        var history = draft.EnableHistory();
        var notifications = new WorkflowNotifications();
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        editor.TryMove(0, 2, out _).ShouldBeTrue();

        await EmitHistorySucceeded(history.Undo, notifications);
        await EmitHistorySucceeded(history.Redo, notifications);

        notifications.ParentCallbacks.ShouldBe(2);
        notifications.SortableListsVersion.ShouldBe(2);
        notifications.VersionsSeenByParent.ShouldBe([1, 2]);
        LiveIds(draft).ShouldBe(["b", "c", "a"]);
        child.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
        history.CanRedo.ShouldBeFalse();
    }

    [Test, Timeout(30_000)]
    public void RemovedDualBindingFiresParentTwiceWithStaleVersionFirst(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Pins the diagnosed pre-fix call graph: ReportStatus ignores its
        // argument and notifies before the version advances, then
        // OnHistoryChanged notifies again. The fix removes this binding.
        var (_, parent) = WorkflowParent("a", "b", "c");
        var child = parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow);
        var draft = child.Session;
        var history = draft.EnableHistory();
        var notifications = new WorkflowNotifications();
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        editor.TryMove(0, 2, out _).ShouldBeTrue();

        history.Undo();
        notifications.ReportStatus(false);
        notifications.OnHistoryChanged();

        notifications.ParentCallbacks.ShouldBe(2);
        notifications.VersionsSeenByParent.ShouldBe([0, 1]);
    }

    [Test, Timeout(30_000)]
    public async Task FailedHistoryActionsNotifyNothing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, parent) = WorkflowParent("a", "b");
        var child = parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow);
        var history = child.Session.EnableHistory();
        var notifications = new WorkflowNotifications();

        history.TryUndo(out _).ShouldBeFalse();
        history.TryRedo(out _).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => history.Undo());
        Should.Throw<InvalidOperationException>(() => history.Redo());

        // ChildSessionActions catches the throw and fires neither event.
        notifications.ParentCallbacks.ShouldBe(0);
        notifications.SortableListsVersion.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test, Timeout(30_000)]
    public async Task AcceptAndDiscardStillRefreshThroughFinishedRoute(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (live, parent) = WorkflowParent("a", "b");
        var child = parent.OpenChildSession(EnterpriseCatalogItem.SparsePath.Workflow);
        var draft = child.Session;
        var notifications = new WorkflowNotifications();
        var editor = draft.EditCollection<CatalogWorkStep, string>(
            CatalogWorkflow.SparsePath.Steps
        );
        editor.TryMove(0, 1, out _).ShouldBeTrue();

        child.TryAcceptChanges(out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        live.Workflow.Steps.Select(static step => step.Id).ShouldBe(["b", "a"]);
        await notifications.RefreshWorkflow();
        notifications.ParentCallbacks.ShouldBe(1);
        notifications.SortableListsVersion.ShouldBe(1);

        editor.TryMove(0, 1, out _).ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        child.DiscardChanges();
        child.HasChanges.ShouldBeFalse();
        await notifications.RefreshWorkflow();
        notifications.ParentCallbacks.ShouldBe(2);
        notifications.SortableListsVersion.ShouldBe(2);
        LiveIds(draft).ShouldBe(["b", "a"]);
    }

    [Test, Timeout(30_000)]
    public async Task StatusOnlyReportsStaySingleWithoutVersionBump(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var notifications = new WorkflowNotifications();

        // Mirrors CatalogDraftStatus reporting a dirty-state transition from
        // a non-history field edit: one unversioned parent notification.
        await notifications.ReportStatus(true);
        await notifications.ReportStatus(false);

        notifications.ParentCallbacks.ShouldBe(2);
        notifications.SortableListsVersion.ShouldBe(0);
        notifications.VersionsSeenByParent.ShouldBe([0, 0]);
    }
}
