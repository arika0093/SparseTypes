using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Sync-plan addition batching over SQLite with 1,000-row workloads.</summary>
/// <remarks>
/// Plan-local identity sets keep duplicate validation linear per batch. The
/// generated change-set pump already rejects duplicate identities inside one
/// transition, so these tests validate large assigned, temporary, and mixed
/// batches plus plan-scoped identity reuse across sibling scopes; the
/// mapper-level duplicate checks stay as defense for change sets from
/// non-generated sources.
/// </remarks>
public sealed class BulkAdditionPlanningTests
{
    private static TemporaryCompositeLineDto Assigned(string tenant, int lineNo, int qty) =>
        new()
        {
            TenantId = tenant,
            LineNo = lineNo,
            Qty = qty,
            Key = tenant + ":" + lineNo,
        };

    private static OrderLineDto TempLine(Guid identity, int quantity, decimal unitPrice) =>
        new()
        {
            Quantity = quantity,
            UnitPrice = unitPrice,
            TemporaryId = identity,
        };

    private static TemporaryCompositeOrderDto ReadWarehouse(TemporaryCompositeOrderEntity order) =>
        new()
        {
            Lines = order
                .Lines.Select(static line => new TemporaryCompositeLineDto
                {
                    TenantId = line.TenantId,
                    LineNo = line.LineNo,
                    Qty = line.Qty,
                    Key = line.TenantId + ":" + line.LineNo,
                })
                .ToList(),
        };

    private static OrderDto ReadOrder(OrderEntity order) =>
        new()
        {
            Name = order.CustomerName,
            Code = order.Code,
            Lines = order
                .Lines.Select(static line => new OrderLineDto
                {
                    Id = line.Id,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                })
                .ToList(),
        };

    private static async Task<TemporaryCompositeOrderEntity> SeedWarehouseAsync(
        KeyedMappingContext context
    )
    {
        var entity = context.TemporaryCompositeOrders.Add(new TemporaryCompositeOrderEntity());
        await context.SaveChangesAsync();
        await context.Entry(entity.Entity).Collection(static order => order.Lines).LoadAsync();
        return entity.Entity;
    }

    private static async Task<OrderEntity> SeedOrderAsync(SampleDbContext context)
    {
        var entity = context.Orders.Add(new OrderEntity()).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        return entity;
    }

    private static SparseEntityMapper<
        TemporaryCompositeOrderEntity,
        TemporaryCompositeOrderDto
    > CreateWarehouseMapper(KeyedMappingContext context) =>
        SparseEntityMapper<TemporaryCompositeOrderEntity, TemporaryCompositeOrderDto>.Create(
            new TemporaryCompositeOrderMapper(),
            context.Model
        );

    private static SparseEntityMapper<OrderEntity, OrderDto> CreateOrderMapper(
        SampleDbContext context
    ) => SparseEntityMapper<OrderEntity, OrderDto>.Create(new OrderMapper(), context.Model);

    [Test]
    public async Task LargeAssignedBatchPlansAndApplies()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedWarehouseAsync(context);
        var mapper = CreateWarehouseMapper(context);
        var changes = ReadWarehouse(entity).CreateChangeSet(BuildWarehouseBatch(1000));

        mapper.Apply(context, entity, changes);
        await context.SaveChangesAsync();

        entity.Lines.Count.ShouldBe(1000);
        entity.Lines.Count(static line => line.TenantId.StartsWith("batch-")).ShouldBe(1000);
        context.ChangeTracker.Entries<TemporaryCompositeLineEntity>().Count().ShouldBe(1000);
    }

    [Test]
    public async Task LargeTemporaryBatchPlansAndApplies()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = await SeedOrderAsync(context);
        var mapper = CreateOrderMapper(context);
        var incoming = new OrderDto();
        var temporaryIdentities = new List<Guid>(1000);
        for (var index = 0; index < 1000; index++)
        {
            var identity = Guid.NewGuid();
            temporaryIdentities.Add(identity);
            incoming.Lines.Add(TempLine(identity, index, (index % 10) + 1m));
        }
        var changes = new OrderDto().CreateChangeSet(incoming);

        var result = mapper.ApplyWithResult(context, entity, changes);
        await context.SaveChangesAsync();

        entity.Lines.Count.ShouldBe(1000);
        var recorded = result.CreatedByPath["Lines"].Keys.ToList();
        recorded.Count.ShouldBe(1000);
        temporaryIdentities.ShouldAllBe(identity => recorded.Contains(identity));
    }

    [Test]
    public async Task MixedAssignedAndTemporaryBatchPlansAndApplies()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = await SeedOrderAsync(context);
        var mapper = CreateOrderMapper(context);
        var incoming = new OrderDto();
        for (var index = 0; index < 500; index++)
        {
            // Alternating rows exercise the asymmetric checks in one batch:
            // assigned rows are only probed against assigned keys, while
            // unassigned rows are probed against every planned identity.
            incoming.Lines.Add(
                new OrderLineDto
                {
                    Id = 8000 + index,
                    Quantity = index,
                    UnitPrice = 1m,
                }
            );
            incoming.Lines.Add(TempLine(Guid.NewGuid(), 1000 + index, 2m));
        }
        var changes = new OrderDto().CreateChangeSet(incoming);

        var result = mapper.ApplyWithResult(context, entity, changes);
        await context.SaveChangesAsync();

        entity.Lines.Count.ShouldBe(1000);
        var assigned = entity.Lines.Count(static line => line.Quantity < 1000);
        var temporary = entity.Lines.Count(static line => line.Quantity >= 1000);
        assigned.ShouldBe(500);
        temporary.ShouldBe(500);
        result.CreatedByPath["Lines"].Count.ShouldBe(500);
    }

    [Test]
    public async Task SiblingScopesMayReuseTemporaryIdentity()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var first = await SeedOrderAsync(context);
        var second = await SeedOrderAsync(context);
        var shared = Guid.NewGuid();
        var results = new List<SparseEntityApplyResult>();
        foreach (var entity in new[] { first, second })
        {
            var incoming = new OrderDto { Lines = [TempLine(shared, 7, 1m)] };
            results.Add(
                CreateOrderMapper(context)
                    .ApplyWithResult(context, entity, new OrderDto().CreateChangeSet(incoming))
            );
        }
        await context.SaveChangesAsync();

        // The identity sets are scoped to one collection plan: an identity
        // reused by a sibling scope stays correlated to its own entity.
        results[0]
            .CreatedByPath["Lines"][shared]
            .ShouldNotBe(results[1].CreatedByPath["Lines"][shared]);
        (await context.Orders.SelectMany(static order => order.Lines).CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task AdditionValidationScalesLinearlyWithBatchSize()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedWarehouseAsync(context);
        var mapper = CreateWarehouseMapper(context);
        const int small = 1000;
        const int large = 4000;

        // Allocated bytes across assigned-key batches at 1,000 and 4,000
        // rows. Linear validation multiplies the bytes by the row factor
        // (~4x); the previous per-run rescan visited every planned addition
        // for each new row and grows roughly quadratically (~16x).
        var smallBytes = MeasureAdditionAllocations(context, entity, mapper, small);
        var largeBytes = MeasureAdditionAllocations(context, entity, mapper, large);

        (largeBytes / (double)smallBytes).ShouldBeLessThan(20);
    }

    private static TemporaryCompositeOrderDto BuildWarehouseBatch(int count)
    {
        var incoming = new TemporaryCompositeOrderDto();
        for (var index = 0; index < count; index++)
        {
            incoming.Lines.Add(Assigned($"batch-{index}", 1, index));
        }
        return incoming;
    }

    private static long MeasureAdditionAllocations(
        KeyedMappingContext context,
        TemporaryCompositeOrderEntity entity,
        SparseEntityMapper<TemporaryCompositeOrderEntity, TemporaryCompositeOrderDto> mapper,
        int count
    )
    {
        long measured = long.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Each attempt starts from an empty collection so the change set
            // sees only fresh assigned rows.
            entity.Lines.Clear();
            var changes = new TemporaryCompositeOrderDto().CreateChangeSet(
                BuildWarehouseBatch(count)
            );
            _ = GC.GetTotalAllocatedBytes(precise: true);
            var baseline = GC.GetTotalAllocatedBytes(precise: true);
            mapper.Apply(context, entity, changes);
            var after = GC.GetTotalAllocatedBytes(precise: true);
            measured = Math.Min(measured, after - baseline);
            // Re-seed the tracker state after the applied rows: clearing it
            // unloads the navigation, so re-attach with an empty loaded list.
            entity.Lines.Clear();
            context.ChangeTracker.Clear();
            context.Attach(entity);
            context.Entry(entity).Collection(static order => order.Lines).IsLoaded = true;
        }

        return measured;
    }
}
