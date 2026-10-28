using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Mapping;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class IncludeFilterBudgetTests
{
    [Test]
    public void OversizedFilterFallsBackBeforeBuildingAnExpressionTree()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var collector = new IncludePathCollector();
        Expression<Func<OrderLineEntity, int>> selector = line => line.Id;
        var keys = Enumerable.Range(1, IncludePathCollector.MaximumFilterKeys + 1).Cast<object>().ToArray();
        collector.PushCollection(
            "Lines",
            typeof(OrderLineEntity),
            false,
            true,
            selector,
            typeof(int),
            context.Model.FindEntityType(typeof(OrderLineEntity)),
            keys
        );
        collector.IncludeCurrent();
        collector.Pop();

        var node = collector.GetPaths().ShouldHaveSingleItem().ShouldHaveSingleItem();
        node.IsUnfiltered.ShouldBeTrue();
        node.Filter.ShouldBeNull();
        collector.Apply(context.Orders).ToQueryString().ShouldNotContain(" OR ");
    }

    [Test]
    public void RepeatedRequestsForOneNavigationCannotBypassTheBudget()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var collector = new IncludePathCollector();
        Expression<Func<OrderLineEntity, int>> selector = line => line.Id;
        var entityType = context.Model.FindEntityType(typeof(OrderLineEntity));
        var keys = Enumerable.Range(1, IncludePathCollector.MaximumFilterKeys / 2).Cast<object>().ToArray();
        for (var request = 0; request < 3; request++)
        {
            collector.PushCollection("Lines", typeof(OrderLineEntity), false, true, selector, typeof(int), entityType, keys);
            collector.IncludeCurrent();
            collector.Pop();
        }

        var node = collector.GetPaths().ShouldHaveSingleItem().ShouldHaveSingleItem();
        node.IsUnfiltered.ShouldBeTrue();
        node.Filter.ShouldBeNull();
    }

    [Test]
    public void DifferentNavigationsShareOneQueryBudget()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = new FilterTokenContext(new DbContextOptionsBuilder<FilterTokenContext>().UseSqlite(connection).Options);
        var collector = new IncludePathCollector();
        Expression<Func<FilterTokenEntity, Guid>> tokenSelector = token => token.Signature;
        Expression<Func<FilterLabelEntity, string>> labelSelector = label => label.Code;
        var tokens = Enumerable.Range(0, IncludePathCollector.MaximumFilterKeys).Select(static _ => (object)Guid.NewGuid()).ToArray();
        collector.PushCollection(
            "Tokens",
            typeof(FilterTokenEntity),
            false,
            true,
            tokenSelector,
            typeof(Guid),
            context.Model.FindEntityType(typeof(FilterTokenEntity)),
            tokens
        );
        collector.IncludeCurrent();
        collector.Pop();
        collector.PushCollection(
            "Labels",
            typeof(FilterLabelEntity),
            false,
            true,
            labelSelector,
            typeof(string),
            context.Model.FindEntityType(typeof(FilterLabelEntity)),
            ["overflow"]
        );
        collector.IncludeCurrent();
        collector.Pop();

        var paths = collector.GetPaths();
        paths.Count.ShouldBe(2);
        paths[0][0].Filter.ShouldNotBeNull();
        paths[1][0].IsUnfiltered.ShouldBeTrue();
        paths[1][0].Filter.ShouldBeNull();
        _ = collector.Apply(context.Boxes).ToQueryString();
    }
}
