using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Nested PassThrough children record into the root history, so the leaf
// context must route Undo/Redo to that same owner.
public sealed class NestedPassThroughHistoryTests
{
    private sealed class Harness : IDisposable
    {
        public ScopeGrandchildRoot.EditSession Root { get; init; } = null!;
        public ISparseEditHistory RootHistory { get; init; } = null!;
        public ISparseChildEditContext<ScopeGrandchildChild, SparseConflict> Child { get; set; } = null!;
        public ISparseChildEditContext<ScopeGrandchildLeaf, SparseConflict> Leaf { get; set; } = null!;
        public Headless.Tree Tree { get; set; } = null!;

        public void Dispose() => Tree.Dispose();
    }

    private static async Task<Harness> CreateAsync(SparseChildEditMode mode)
    {
        var root = new ScopeGrandchildRoot
        {
            Title = "Root",
            Child = new ScopeGrandchildChild { Leaf = new ScopeGrandchildLeaf { Name = "Baseline" } },
        };
        var session = root.CreateEditSession();
        var harness = new Harness { Root = session, RootHistory = session.EnableHistory() };

        RenderFragment content = builder =>
        {
            builder.OpenComponent<SparseSessionScope<ScopeGrandchildRoot>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<ScopeGrandchildRoot>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<ScopeGrandchildRoot>.Mode), mode);
            builder.AddAttribute(
                3,
                nameof(SparseSessionScope<ScopeGrandchildRoot>.ChildContent),
                ChildFragment(
                    (ISparseEditSessionWithChildren<ScopeGrandchildRoot, SparseConflict>)session,
                    ScopeGrandchildRoot.SparsePath.Child,
                    child =>
                    {
                        harness.Child = child;
                        return ChildFragment(
                            (ISparseEditSessionWithChildren<ScopeGrandchildChild, SparseConflict>)child.Session,
                            ScopeGrandchildChild.SparsePath.Leaf,
                            leaf =>
                            {
                                harness.Leaf = leaf;
                                return static _ => { };
                            }
                        );
                    }
                )
            );
            builder.CloseComponent();
        };
        harness.Tree = await Headless.Tree.CreateAsync(content);
        return harness;
    }

    private static RenderFragment ChildFragment<TParent, TChild, TPaths, TConflict, TSession>(
        ISparseEditSessionWithChildren<TParent, TConflict> parent,
        ISparseChildEditPath<TParent, TChild, TPaths, TSession> path,
        Func<SparseChildEditContext<TChild, TPaths, TConflict, TSession>, RenderFragment> content
    )
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TSession : ISparseEditSessionWithPaths<TChild, TPaths> =>
        builder =>
        {
            builder.OpenComponent<SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>>(0);
            builder.AddAttribute(1, nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.Path), path);
            builder.AddAttribute(
                2,
                nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.ChildContent),
                (RenderFragment<SparseChildEditContext<TChild, TPaths, TConflict, TSession>>)(context => content(context))
            );
            builder.CloseComponent();
        };

    [Test]
    public async Task PassThroughLeafUndoRedoRouteToTheRootHistory()
    {
        using var harness = await CreateAsync(SparseChildEditMode.PassThrough);

        ((ScopeGrandchildLeaf.EditSession)harness.Leaf.Session).Observable.Name = "Draft";
        await harness.Tree.FlushAsync();

        harness.RootHistory.CanUndo.ShouldBeTrue();
        harness.Leaf.CanUndo.ShouldBeTrue();
        harness.Child.CanUndo.ShouldBeTrue();

        harness.Leaf.Undo();
        harness.Root.GetModelForFrameworkAccess().Child.Leaf.Name.ShouldBe("Baseline");
        harness.Leaf.CanRedo.ShouldBeTrue();

        harness.Leaf.Redo();
        harness.Root.GetModelForFrameworkAccess().Child.Leaf.Name.ShouldBe("Draft");
    }

    [Test]
    public async Task DraftLeafKeepsItsOwnHistory()
    {
        using var harness = await CreateAsync(SparseChildEditMode.Draft);

        ((ScopeGrandchildLeaf.EditSession)harness.Leaf.Session).Observable.Name = "Draft";
        await harness.Tree.FlushAsync();

        harness.Leaf.CanUndo.ShouldBeTrue();
        harness.RootHistory.CanUndo.ShouldBeFalse();
        harness.Child.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void DiscardingChildSessionsToleratesSiblingRemovalDuringCallbacks()
    {
        var root = new CoordinatorMutationRoot
        {
            First = new CoordinatorMutationChild { Value = "First" },
            Second = new CoordinatorMutationChild { Value = "Second" },
        };
        var session = root.CreateEditSession();
        var parent = (ISparseEditSessionWithChildren<CoordinatorMutationRoot, SparseConflict>)session;
        var first = parent.OpenChildSession(CoordinatorMutationRoot.SparsePath.First);
        var second = parent.OpenChildSession(CoordinatorMutationRoot.SparsePath.Second);
        first.Session.Observable.Value = "Edited first";
        second.Session.Observable.Value = "Edited second";
        ((System.ComponentModel.INotifyPropertyChanged)first.Session).PropertyChanged += (_, _) => second.Dispose();

        parent.DiscardChildSessions();
    }

    [Test]
    public void ConfirmingChildSessionsToleratesSiblingRemovalDuringCallbacks()
    {
        var root = new CoordinatorMutationRoot
        {
            First = new CoordinatorMutationChild { Value = "First" },
            Second = new CoordinatorMutationChild { Value = "Second" },
        };
        var session = root.CreateEditSession();
        var parent = (ISparseEditSessionWithChildren<CoordinatorMutationRoot, SparseConflict>)session;
        var first = parent.OpenChildSession(CoordinatorMutationRoot.SparsePath.First);
        var second = parent.OpenChildSession(CoordinatorMutationRoot.SparsePath.Second);
        first.Session.Observable.Value = "Edited first";
        second.Session.Observable.Value = "Edited second";
        ((System.ComponentModel.INotifyPropertyChanged)first.Session).PropertyChanged += (_, _) => second.Dispose();

        parent.ConfirmChildSessions();
    }
}

[SparseModel]
public partial class CoordinatorMutationChild
{
    public string Value { get; set; } = string.Empty;
}

[SparseModel]
public partial class CoordinatorMutationRoot
{
    public CoordinatorMutationChild First { get; set; } = new();

    public CoordinatorMutationChild Second { get; set; } = new();
}
