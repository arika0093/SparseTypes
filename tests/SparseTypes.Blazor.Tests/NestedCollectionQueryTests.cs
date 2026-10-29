using SparseTypes.Generated;

namespace SparseTypes.Blazor.Tests;

public sealed class NestedCollectionQueryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void KeyedAndTemporaryRowsReadTheCurrentNestedChanges(bool temporary)
    {
        var temporaryId = Guid.NewGuid();
        var path = temporary
            ? CollectionQueryRoot.SparsePath.Nodes.TemporaryKey(temporaryId).Members.UntypedPath
            : CollectionQueryRoot.SparsePath.Nodes.Key(1).Members.UntypedPath;

        for (var iteration = 0; iteration < 2; iteration++)
        {
            var session = new CollectionQueryRoot
            {
                Nodes = [new CollectionQueryNode { Id = temporary ? 0 : 1, TemporaryId = temporary ? temporaryId : null }],
            }.CreateEditSession();
            var raw = ((ISparseEditSession<CollectionQueryRoot>)session).Model;
            var key = "member-" + iteration;
            raw.Nodes[0].Members.Add(new TeamMember { Id = key });

            var state = Query(session, session.PreviewChangeSet(), path);
            state.IsChanged.ShouldBeTrue();
            state.GetItemState(key).IsAdded.ShouldBeTrue();
            state.GetItemState("absent").IsEmpty.ShouldBeTrue();

            session.AcceptChanges();
            Query(session, session.PreviewChangeSet(), path).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void StructuralDictionaryRowsExposeNestedKeyedAndScalarDictionaryChanges()
    {
        var session = new CollectionQueryRoot
        {
            ByName =
            {
                ["node"] = new CollectionQueryNode { Id = 1, Scores = { ["score"] = 1 } },
            },
        }.CreateEditSession();
        var raw = ((ISparseEditSession<CollectionQueryRoot>)session).Model;
        raw.ByName["node"].Members.Add(new TeamMember { Id = "added" });
        raw.ByName["node"].Scores["score"] = 2;
        raw.Scores["root"] = 3;
        var changes = session.PreviewChangeSet();

        Query(session, changes, CollectionQueryRoot.SparsePath.ByName.Key("node").Members.UntypedPath).GetItemState("added").IsAdded.ShouldBeTrue();
        Query(session, changes, CollectionQueryRoot.SparsePath.ByName.Key("node").Scores.UntypedPath).GetItemState("score").IsEdited.ShouldBeTrue();
        Query(session, changes, CollectionQueryRoot.SparsePath.Scores.UntypedPath).GetItemState("root").IsAdded.ShouldBeTrue();
        Query(session, changes, CollectionQueryRoot.SparsePath.ByName.UntypedPath).GetItemState("node").IsEdited.ShouldBeTrue();

        var missing = CollectionQueryRoot.SparsePath.ByName.Key("absent").Members.UntypedPath;
        SparseNestedCollectionStateQuery.TryGetState<CollectionQueryRoot>(changes, Descriptors(session), missing, out _).ShouldBeFalse();
    }

    private static SparseCollectionState Query(object session, ISparseChangeSet changes, SparsePath path)
    {
        SparseNestedCollectionStateQuery.TryGetState<CollectionQueryRoot>(changes, Descriptors(session), path, out var state).ShouldBeTrue();
        return state.ShouldNotBeNull();
    }

    private static IDescriptorSet Descriptors(object session) => (IDescriptorSet)((ISparseEditSessionWithDescriptors)session).Descriptors;
}

[SparseModel]
public partial class CollectionQueryRoot
{
    public List<CollectionQueryNode> Nodes { get; set; } = [];
    public Dictionary<string, CollectionQueryNode> ByName { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = [];
}

[SparseModel]
public partial class CollectionQueryNode
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public List<TeamMember> Members { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = [];
}
