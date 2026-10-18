using BenchmarkDotNet.Attributes;
using SparseTypes;

// Rebase-resolution benchmarks for the ChangeSet/rebase workstream: indexed
// decision lookup (#320), indexed order application (#334), per-build
// collection staging (#335), and push/pop change enumeration (#336).
// Setups validate conflict shapes and input non-mutation; several bodies
// include the trailing Apply so checksums observe end-to-end resolution.

/// <summary>Indexed conflict/decision selection behind TryBuild.</summary>
[MemoryDiagnoser]
public class RebaseDecisionBenchmarks
{
    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    private RebaseResult<BenchValueKeyedHolder.ChangeSet> _rebase = null!;
    private BenchValueKeyedHolder _currentModel = null!;
    private SparsePath[] _paths = [];
    private BenchValueKeyedHolder.ChangeSet.Resolution _decided = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Items(0);
        var desired = Items(-1);
        var current = Items(-2);
        _currentModel = new BenchValueKeyedHolder { Items = current };
        _rebase = BenchValueKeyedHolder
            .ChangeSet.Between(State(before), State(desired))
            .RebaseOnto(State(current));
        if (_rebase.Conflicts.Count != Size)
        {
            throw new InvalidOperationException(
                "Decision benchmarks need one leaf conflict per row."
            );
        }

        _paths = _rebase.Conflicts.Select(static conflict => conflict.Path).ToArray();
        _decided = Decide();
        if (_decided.EnumerateUnresolvedConflicts().Count != 0)
        {
            throw new InvalidOperationException("Setup decisions must cover every conflict.");
        }
    }

    private List<BenchValueKeyedItem> Items(int count) =>
        Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = count })
            .ToList();

    private static Optional<BenchValueKeyedHolder.Fragment?> State(
        List<BenchValueKeyedItem> values
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(values),
            }
        );

    private BenchValueKeyedHolder.ChangeSet.Resolution Decide()
    {
        var resolution = BenchValueKeyedHolder.ChangeSet.BeginResolution(_rebase, _currentModel);
        foreach (var path in _paths)
        {
            resolution.UseIncoming(path);
        }

        return resolution;
    }

    [Benchmark]
    public int RegisterDecisions() => Decide().DecidedCount;

    [Benchmark]
    public int QueryAllStates()
    {
        var checksum = 0;
        foreach (var path in _paths)
        {
            checksum += (int)_decided.GetState(path);
        }

        return checksum;
    }

    [Benchmark]
    public int EnumerateUnresolved() => _decided.EnumerateUnresolvedConflicts().Count;
}

public enum RebaseOrderScenario
{
    Reverse,
    Divergent,
}

/// <summary>Indexed keyed order application for one order conflict.</summary>
[MemoryDiagnoser]
public class RebaseOrderBenchmarks
{
    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    [Params(RebaseOrderScenario.Reverse, RebaseOrderScenario.Divergent)]
    public RebaseOrderScenario Scenario { get; set; }

    private RebaseResult<BenchValueKeyedHolder.ChangeSet> _rebase = null!;
    private BenchValueKeyedHolder _currentModel = null!;
    private SparsePath _orderPath;
    private List<int> _desiredOrder = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = RebaseIds(Enumerable.Range(0, Size));
        var desired = RebaseIds(Enumerable.Range(0, Size).Reverse());
        var current =
            Scenario == RebaseOrderScenario.Divergent
                ? RebaseIds(Enumerable.Range(0, Size).Skip(1).Append(0))
                : RebaseIds(Enumerable.Range(0, Size).Append(Size));
        _currentModel = new BenchValueKeyedHolder { Items = current };
        _rebase = BenchValueKeyedHolder
            .ChangeSet.Between(OrderState(before), OrderState(desired))
            .RebaseOnto(OrderState(current));
        if (_rebase.Conflicts.Count != 1)
        {
            throw new InvalidOperationException(
                "Order benchmarks need exactly one order conflict."
            );
        }

        _orderPath = _rebase.Conflicts[0].Path;
        _desiredOrder = desired.Select(static item => item.Id).ToList();
    }

    private static List<BenchValueKeyedItem> RebaseIds(IEnumerable<int> ids) =>
        ids.Select(index => new BenchValueKeyedItem { Id = index, Count = index }).ToList();

    private static Optional<BenchValueKeyedHolder.Fragment?> OrderState(
        List<BenchValueKeyedItem> values
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(values),
            }
        );

    [Benchmark]
    public int TryBuildOrder()
    {
        var resolution = BenchValueKeyedHolder.ChangeSet.BeginResolution(_rebase, _currentModel);
        resolution.UseIncoming(_orderPath);
        if (!resolution.TryBuild(out var resolved, out var failure))
        {
            throw new InvalidOperationException("Order resolution must build: " + failure?.Reason);
        }

        var applied = resolved!
            .ToPatch()
            .Apply(OrderState(_currentModel.Items))
            .Value!.Items.Value!;
        var checksum = applied.Count;
        for (var index = 0; index < _desiredOrder.Count; index++)
        {
            checksum += applied[index].Id == _desiredOrder[index] ? 0 : 1;
        }

        return checksum;
    }
}

/// <summary>Per-build collection staging across bulk leaf choices.</summary>
/// <remarks>One leaf conflict per row; rows carry a single leaf, so the
/// many-leaves-on-one-row sub-case has no bench shape and stays unmeasured.</remarks>
[MemoryDiagnoser]
public class RebaseBulkBenchmarks
{
    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    private RebaseResult<BenchValueKeyedHolder.ChangeSet> _rebase = null!;
    private BenchValueKeyedHolder _currentModel = null!;
    private SparsePath[] _paths = [];

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = index })
            .ToList();
        var desired = before.Select(static item => item with { Count = -1 }).ToList();
        var current = before.Select(static item => item with { Count = -2 }).ToList();
        _currentModel = new BenchValueKeyedHolder { Items = current };
        _rebase = BenchValueKeyedHolder
            .ChangeSet.Between(BulkState(before), BulkState(desired))
            .RebaseOnto(BulkState(current));
        if (_rebase.Conflicts.Count != Size)
        {
            throw new InvalidOperationException("Bulk benchmarks need one conflict per row.");
        }

        _paths = _rebase.Conflicts.Select(static conflict => conflict.Path).ToArray();
    }

    private static Optional<BenchValueKeyedHolder.Fragment?> BulkState(
        List<BenchValueKeyedItem> values
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(values),
            }
        );

    [Benchmark]
    public int TryBuildBulkLeaves()
    {
        var resolution = BenchValueKeyedHolder.ChangeSet.BeginResolution(_rebase, _currentModel);
        foreach (var path in _paths)
        {
            resolution.UseIncoming(path);
        }

        if (!resolution.TryBuild(out var resolved, out var failure))
        {
            throw new InvalidOperationException("Bulk resolution must build: " + failure?.Reason);
        }

        var applied = resolved!.ToPatch().Apply(BulkState(_currentModel.Items)).Value!.Items.Value!;
        var checksum = applied.Count;
        foreach (var item in applied)
        {
            checksum += item.Count;
        }

        return checksum;
    }
}

[SparseModel]
public partial class TravDetail
{
    public string Note { get; set; } = string.Empty;
}

[SparseModel]
public partial class TravGroup
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TravDetail Detail { get; set; } = new();
}

[SparseModel]
public partial class TravNestedHolder
{
    public List<TravGroup> Groups { get; set; } = new();
}

public enum ChangeSetTraversalShape
{
    DeepKeyed,
    FlatKeyed,
}

/// <summary>Push/pop change enumeration over nested and flat keyed models.</summary>
/// <remarks>Depths beyond the available generated shapes stay analytic: no
/// 16/64-deep model exists in this project.</remarks>
[MemoryDiagnoser]
public class ChangeSetTraversalBenchmarks
{
    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    [Params(ChangeSetTraversalShape.DeepKeyed, ChangeSetTraversalShape.FlatKeyed)]
    public ChangeSetTraversalShape Shape { get; set; }

    private TravNestedHolder.ChangeSet _nested = null!;
    private BenchValueKeyedHolder.ChangeSet _flat = null!;
    private int _expected;

    [GlobalSetup]
    public void Setup()
    {
        var before = TravGroups(0);
        var after = TravGroups(1).AsEnumerable().Reverse().ToList();
        _nested = TravNestedHolder.ChangeSet.Between(TravState(before), TravState(after));
        var flatBefore = Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = index })
            .ToList();
        var flatAfter = flatBefore
            .Select(static item => item with { Count = item.Count + 1 })
            .AsEnumerable()
            .Reverse()
            .ToList();
        _flat = BenchValueKeyedHolder.ChangeSet.Between(
            TravFlatState(flatBefore),
            TravFlatState(flatAfter)
        );
        var nestedCount = _nested.EnumerateChanges().Count();
        var flatCount = _flat.EnumerateChanges().Count();
        _expected = Shape == ChangeSetTraversalShape.DeepKeyed ? nestedCount : flatCount;
        if (nestedCount != 2 * Size + 1 || flatCount != Size + 1)
        {
            throw new InvalidOperationException(
                "Traversal benchmarks need leaves at two depths plus the order entry."
            );
        }
    }

    private List<TravGroup> TravGroups(int step) =>
        Enumerable
            .Range(0, Size)
            .Select(index => new TravGroup
            {
                Id = "group-" + index,
                Name = "name-" + step,
                Detail = new TravDetail { Note = "note-" + step },
            })
            .ToList();

    private static Optional<TravNestedHolder.Fragment?> TravState(List<TravGroup> values) =>
        Optional<TravNestedHolder.Fragment?>.Present(
            new TravNestedHolder.Fragment { Groups = Optional<List<TravGroup>>.Present(values) }
        );

    private static Optional<BenchValueKeyedHolder.Fragment?> TravFlatState(
        List<BenchValueKeyedItem> values
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(values),
            }
        );

    [Benchmark]
    public int Enumerate()
    {
        var checksum = 0;
        if (Shape == ChangeSetTraversalShape.DeepKeyed)
        {
            foreach (var change in _nested.EnumerateChanges())
            {
                checksum +=
                    change.PathText.Length
                    + (int)change.Kind
                    + (change.Before.IsPresent ? 1 : 0)
                    + (change.After.IsPresent ? 1 : 0);
            }
        }
        else
        {
            foreach (var change in _flat.EnumerateChanges())
            {
                checksum +=
                    change.PathText.Length
                    + (int)change.Kind
                    + (change.Before.IsPresent ? 1 : 0)
                    + (change.After.IsPresent ? 1 : 0);
            }
        }

        if (checksum == 0 || _expected == 0)
        {
            throw new InvalidOperationException("Traversal enumeration must observe entries.");
        }

        return checksum;
    }
}
