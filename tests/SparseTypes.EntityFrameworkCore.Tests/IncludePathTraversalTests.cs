using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies single-pass Include path discovery: exact emitted paths across
/// owned-prefix, chained, branched, and wide shapes, plus equivalence with
/// the previous repeated-scan traversal and its quadratic predicate cost.
/// </summary>
public sealed class IncludePathTraversalTests
{
    [Test]
    public void DeepOwnedPrefixWithTerminalIncludedEmitsSinglePath()
    {
        var collector = new IncludePathCollector();
        const int ownedDepth = 50;
        for (var index = 0; index < ownedDepth; index++)
        {
            collector.PushReference("Owned" + index, typeof(object), owned: true);
        }

        collector.PushReference("Target", typeof(object), owned: false);
        collector.IncludeCurrent();
        for (var index = 0; index <= ownedDepth; index++)
        {
            collector.Pop();
        }

        var expected = string.Join(
            ".",
            Enumerable.Range(0, ownedDepth).Select(index => "Owned" + index).Append("Target")
        );
        DottedPaths(collector).ShouldBe([expected]);
    }

    [Test]
    public void DeepOwnedPrefixWithTerminalCollectionEmitsSinglePath()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Owned", typeof(object), owned: true);
        collector.PushCollection(
            "Lines",
            typeof(object),
            owned: false,
            filterEdits: false,
            keySelector: null,
            keyType: null,
            entityType: null,
            keys: null
        );
        collector.IncludeCurrent();
        collector.Pop();
        collector.Pop();

        DottedPaths(collector).ShouldBe(["Owned.Lines"]);
    }

    [Test]
    public void AllIncludedChainEmitsOnlyDeepestPath()
    {
        var collector = new IncludePathCollector();
        const int depth = 25;
        for (var index = 0; index < depth; index++)
        {
            collector.PushReference("Node" + index, typeof(object), owned: false);
            collector.IncludeCurrent();
        }

        for (var index = 0; index < depth; index++)
        {
            collector.Pop();
        }

        DottedPaths(collector)
            .ShouldBe([
                string.Join(".", Enumerable.Range(0, depth).Select(index => "Node" + index)),
            ]);
    }

    [Test]
    public void OwnedPrefixAloneEmitsNothing()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Owned", typeof(object), owned: true);
        collector.IncludeCurrent();
        collector.Pop();

        DottedPaths(collector).ShouldBeEmpty();
    }

    [Test]
    public void IncludedRootWithoutDescendantsEmitsSingleSegment()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Root", typeof(object), owned: false);
        collector.IncludeCurrent();
        collector.Pop();

        DottedPaths(collector).ShouldBe(["Root"]);
    }

    [Test]
    public void NoIncludedNodesEmitsNothing()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Root", typeof(object), owned: false);
        collector.PushReference("Child", typeof(object), owned: false);
        collector.Pop();
        collector.Pop();

        DottedPaths(collector).ShouldBeEmpty();
    }

    [Test]
    public void IncludedAncestorSuppressedByIncludedDescendant()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Ancestor", typeof(object), owned: false);
        collector.IncludeCurrent();
        collector.PushReference("Middle", typeof(object), owned: true);
        collector.PushReference("Leaf", typeof(object), owned: false);
        collector.IncludeCurrent();
        collector.Pop();
        collector.Pop();
        collector.PushReference("Sibling", typeof(object), owned: false);
        collector.IncludeCurrent();
        collector.Pop();
        collector.Pop();

        DottedPaths(collector).ShouldBe(["Ancestor.Middle.Leaf", "Ancestor.Sibling"]);
    }

    [Test]
    public void MultipleBranchesEmitInChildOrder()
    {
        var collector = new IncludePathCollector();
        collector.PushReference("Root", typeof(object), owned: false);
        PushIncludedLeaf(collector, "First");
        collector.PushReference("Skipped", typeof(object), owned: false);
        collector.Pop();
        collector.PushReference("Nested", typeof(object), owned: false);
        PushIncludedLeaf(collector, "Deep1");
        PushIncludedLeaf(collector, "Deep2");
        collector.Pop();
        PushIncludedLeaf(collector, "Last");
        collector.Pop();

        DottedPaths(collector)
            .ShouldBe(["Root.First", "Root.Nested.Deep1", "Root.Nested.Deep2", "Root.Last"]);
    }

    [Test]
    public void WideTreeEmitsExactOrderedPaths()
    {
        var collector = new IncludePathCollector();
        const int width = 300;
        collector.PushReference("Root", typeof(object), owned: false);
        var expected = new List<string>();
        for (var index = 0; index < width; index++)
        {
            collector.PushReference("Child" + index, typeof(object), owned: false);
            if (index % 3 == 0)
            {
                collector.IncludeCurrent();
                expected.Add("Root.Child" + index);
            }

            collector.Pop();
        }

        collector.Pop();

        DottedPaths(collector).ShouldBe(expected.ToArray());
    }

    [Test]
    public void TerminalChainsMatchLegacyOracleAndLegacyCostIsQuadratic()
    {
        foreach (var depth in new[] { 10, 100, 500 })
        {
            var collector = BuildTerminalChain(depth);
            var probe = BuildTerminalProbe(depth);

            var calls = new CallCounter();
            var legacy = LegacyPaths(probe, calls);

            DottedPaths(collector).ShouldBe(legacy);
            calls.Count.ShouldBe(depth * (depth - 1));
        }
    }

    [Test]
    public void MixedTreesMatchLegacyOracle()
    {
        var random = new Random(42);
        for (var tree = 0; tree < 200; tree++)
        {
            var shape = CreateMixedShape(random, 100);
            var collector = new IncludePathCollector();
            EmitShape(collector, shape, 0);

            var calls = new CallCounter();
            var legacy = LegacyPaths(shape.Probe, calls);

            DottedPaths(collector).ShouldBe(legacy);
        }
    }

    [Test]
    public void MergedCollectionFilterSurvivesTraversal()
    {
        var entityType = LineEntityType();
        Expression<Func<TraversalLineEntity, int>> keySelector = line => line.Id;
        var collector = new IncludePathCollector();
        collector.PushCollection(
            "Lines",
            typeof(TraversalLineEntity),
            owned: false,
            filterEdits: true,
            keySelector,
            typeof(int),
            entityType,
            keys: [1, 2]
        );
        collector.IncludeCurrent();
        collector.Pop();
        collector.PushCollection(
            "Lines",
            typeof(TraversalLineEntity),
            owned: false,
            filterEdits: true,
            keySelector,
            typeof(int),
            entityType,
            keys: [2, 3]
        );
        collector.IncludeCurrent();
        collector.Pop();

        DottedPaths(collector).ShouldBe(["Lines"]);

        var paths = collector.GetPaths();
        paths.ShouldHaveSingleItem();
        var node = paths[0][0];
        node.Filter.ShouldNotBeNull();
        var predicate = node.Filter.BuildPredicate();
        KeyConstants(predicate).ShouldBe([1, 2, 3]);
    }

    [Test]
    public void BalancedPredicateShapePreservedForMergedFilter()
    {
        var entityType = LineEntityType();
        Expression<Func<TraversalLineEntity, int>> keySelector = line => line.Id;
        var collector = new IncludePathCollector();
        collector.PushCollection(
            "Lines",
            typeof(TraversalLineEntity),
            owned: false,
            filterEdits: true,
            keySelector,
            typeof(int),
            entityType,
            keys: [1, 2, 3, 4]
        );
        collector.IncludeCurrent();
        collector.Pop();
        collector.PushCollection(
            "Lines",
            typeof(TraversalLineEntity),
            owned: false,
            filterEdits: true,
            keySelector,
            typeof(int),
            entityType,
            keys: [5, 6, 7, 8]
        );
        collector.IncludeCurrent();
        collector.Pop();

        var paths = collector.GetPaths();
        paths.ShouldHaveSingleItem();
        var node = paths[0][0];
        node.Filter.ShouldNotBeNull();
        var predicate = node.Filter.BuildPredicate();
        KeyConstants(predicate).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
        OrElseDepth(predicate).ShouldBeLessThanOrEqualTo(4);
    }

    [Test]
    public void UnfilterableCollectionStillEmitsInclude()
    {
        var collector = new IncludePathCollector();
        collector.PushCollection(
            "Lines",
            typeof(object),
            owned: false,
            filterEdits: true,
            keySelector: null,
            keyType: null,
            entityType: null,
            keys: null
        );
        collector.IncludeCurrent();
        collector.Pop();

        DottedPaths(collector).ShouldBe(["Lines"]);

        var paths = collector.GetPaths();
        paths.ShouldHaveSingleItem();
        paths[0][0].Filter.ShouldBeNull();
    }

    private static void PushIncludedLeaf(IncludePathCollector collector, string name)
    {
        collector.PushReference(name, typeof(object), owned: false);
        collector.IncludeCurrent();
        collector.Pop();
    }

    private static string[] DottedPaths(IncludePathCollector collector) =>
        collector
            .GetPaths()
            .Select(path => string.Join(".", path.Select(node => node.Name)))
            .ToArray();

    private static IncludePathCollector BuildTerminalChain(int depth)
    {
        var collector = new IncludePathCollector();
        for (var index = 0; index < depth - 1; index++)
        {
            collector.PushReference("Owned" + index, typeof(object), owned: true);
        }

        collector.PushReference("Terminal", typeof(object), owned: false);
        collector.IncludeCurrent();
        for (var index = 0; index < depth; index++)
        {
            collector.Pop();
        }

        return collector;
    }

    private static ProbeNode BuildTerminalProbe(int depth)
    {
        var root = new ProbeNode("Owned0", false);
        var current = root;
        for (var index = 1; index < depth - 1; index++)
        {
            var next = new ProbeNode("Owned" + index, false);
            current.Children.Add(next);
            current = next;
        }

        current.Children.Add(new ProbeNode("Terminal", true));
        return root;
    }

    private sealed class MixedShape
    {
        public required List<int>[] Children { get; init; }

        public required bool[] Owned { get; init; }

        public required bool[] RequestInclude { get; init; }

        public required bool[] IsCollection { get; init; }

        public required ProbeNode Probe { get; init; }
    }

    private static MixedShape CreateMixedShape(Random random, int count)
    {
        var children = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        for (var index = 1; index < count; index++)
        {
            children[random.Next(0, index)].Add(index);
        }

        var owned = new bool[count];
        var requestInclude = new bool[count];
        var isCollection = new bool[count];
        for (var index = 0; index < count; index++)
        {
            owned[index] = random.Next(2) == 0;
            requestInclude[index] = random.Next(3) == 0;
            isCollection[index] = random.Next(4) == 0;
        }

        ProbeNode BuildProbe(int index)
        {
            var node = new ProbeNode("N" + index, requestInclude[index] && !owned[index]);
            foreach (var child in children[index])
            {
                node.Children.Add(BuildProbe(child));
            }

            return node;
        }

        return new MixedShape
        {
            Children = children,
            Owned = owned,
            RequestInclude = requestInclude,
            IsCollection = isCollection,
            Probe = BuildProbe(0),
        };
    }

    private static void EmitShape(IncludePathCollector collector, MixedShape shape, int index)
    {
        if (shape.IsCollection[index])
        {
            collector.PushCollection(
                "N" + index,
                typeof(object),
                shape.Owned[index],
                filterEdits: false,
                keySelector: null,
                keyType: null,
                entityType: null,
                keys: null
            );
        }
        else
        {
            collector.PushReference("N" + index, typeof(object), shape.Owned[index]);
        }

        if (shape.RequestInclude[index])
        {
            collector.IncludeCurrent();
        }

        foreach (var child in shape.Children[index])
        {
            EmitShape(collector, shape, child);
        }

        collector.Pop();
    }

    private sealed class ProbeNode
    {
        public ProbeNode(string name, bool isIncluded)
        {
            Name = name;
            IsIncluded = isIncluded;
        }

        public string Name { get; }

        public bool IsIncluded { get; }

        public List<ProbeNode> Children { get; } = [];
    }

    private sealed class CallCounter
    {
        public int Count;
    }

    // Faithful copy of the traversal replaced by the single-pass collector,
    // kept as an equivalence oracle with a predicate-call counter.
    private static bool LegacyHasIncludedPath(ProbeNode node, CallCounter calls)
    {
        calls.Count++;
        return node.IsIncluded || node.Children.Any(child => LegacyHasIncludedPath(child, calls));
    }

    private static void LegacyCollectLeafPaths(
        ProbeNode node,
        List<ProbeNode> current,
        List<List<ProbeNode>> paths,
        CallCounter calls
    )
    {
        current.Add(node);
        var includedChild = node.Children.Any(child => LegacyHasIncludedPath(child, calls));
        if (node.IsIncluded && !includedChild)
        {
            paths.Add([.. current]);
        }

        foreach (var child in node.Children.Where(child => LegacyHasIncludedPath(child, calls)))
        {
            LegacyCollectLeafPaths(child, current, paths, calls);
        }

        current.RemoveAt(current.Count - 1);
    }

    private static string[] LegacyPaths(ProbeNode root, CallCounter calls)
    {
        var paths = new List<List<ProbeNode>>();
        LegacyCollectLeafPaths(root, [], paths, calls);
        return paths.Select(path => string.Join(".", path.Select(node => node.Name))).ToArray();
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType LineEntityType()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<TraversalLineEntity>(entity => entity.HasKey(line => line.Id));
        return modelBuilder.FinalizeModel().FindEntityType(typeof(TraversalLineEntity))!;
    }

    private static IEnumerable<Expression> Descendants(Expression node)
    {
        yield return node;
        switch (node)
        {
            case LambdaExpression lambda:
                foreach (var child in Descendants(lambda.Body))
                {
                    yield return child;
                }

                break;
            case MethodCallExpression call:
                if (call.Object is not null)
                {
                    foreach (var child in Descendants(call.Object))
                    {
                        yield return child;
                    }
                }

                foreach (var argument in call.Arguments)
                {
                    foreach (var child in Descendants(argument))
                    {
                        yield return child;
                    }
                }

                break;
            case UnaryExpression unary:
                foreach (var child in Descendants(unary.Operand))
                {
                    yield return child;
                }

                break;
            case BinaryExpression binary:
                foreach (var child in Descendants(binary.Left))
                {
                    yield return child;
                }

                foreach (var child in Descendants(binary.Right))
                {
                    yield return child;
                }

                break;
            case MemberExpression member when member.Expression is not null:
                foreach (var child in Descendants(member.Expression))
                {
                    yield return child;
                }

                break;
            case ConditionalExpression conditional:
                foreach (
                    var child in Descendants(conditional.Test)
                        .Concat(Descendants(conditional.IfTrue))
                        .Concat(Descendants(conditional.IfFalse))
                )
                {
                    yield return child;
                }

                break;
            default:
                break;
        }
    }

    private static int[] KeyConstants(Expression predicate) =>
        Descendants(predicate)
            .OfType<ConstantExpression>()
            .Where(constant => constant.Type == typeof(int))
            .Select(constant => (int)constant.Value!)
            .Order()
            .ToArray();

    private static int OrElseDepth(Expression node) =>
        node is BinaryExpression { NodeType: ExpressionType.OrElse } balanced
            ? 1 + Math.Max(OrElseDepth(balanced.Left), OrElseDepth(balanced.Right))
            : 0;
}

/// <summary>Collection element for traversal filter tests.</summary>
public sealed class TraversalLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }
}
