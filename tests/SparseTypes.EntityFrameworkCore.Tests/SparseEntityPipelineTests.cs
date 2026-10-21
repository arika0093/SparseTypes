using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class SparseEntityPipelineTests
{
    [Test]
    public async Task DocumentationSavePipelineSampleRuns() => await SparseEntityPipelineDocsSample.Run();

    private static async Task<OrderDto> ReadOrder(SampleDbContext context, SparseEntityMapper<OrderEntity, OrderDto> mapper, int id) =>
        await context.Orders.AsNoTracking().Where(order => order.Id == id).Select(mapper.Projection).SingleAsync();

    [Test]
    public async Task PipelinePersistsReadbackAndReconcilesSessionAutomatically()
    {
        using var connection = TestDatabases.OpenMemory();
        int id;
        OrderDto baseline;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            id = SeedOrder(seed);
            var seedMapper = PipelineOrderMapper.Create(seed.Model);
            baseline = await ReadOrder(seed, seedMapper, id);
        }

        var draft = new OrderDto
        {
            Name = baseline.Name,
            Notes = baseline.Notes,
            Code = baseline.Code,
        };
        var session = draft.CreateEditSession();
        session.Observable.Notes = "submitted";
        var changes = session.CreateChangeSet();

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = PipelineOrderMapper.Create(context.Model);
        var result = await mapper.PipelineExecuteAsync(
            context,
            changes,
            order => order.Id == id,
            session,
            beforeSave: (save, _) =>
            {
                save.Db.Entry(save.Entity).Property(static order => order.CustomerName).CurrentValue = "Ann";
                session.Observable.Code = "after-submit";
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntityPipelineStage.Saved);
        result.ReadBack!.Notes.ShouldBe("submitted");
        result.ReadBack.Code.ShouldBe("ABC");
        draft.Notes.ShouldBe("submitted");
        draft.Code.ShouldBe("after-submit");
        session.HasChanges.ShouldBeTrue();
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        (await fresh.Orders.SingleAsync(order => order.Id == id)).Notes.ShouldBe("submitted");
    }

    [Test]
    public async Task PipelineReturnsSemanticConflictWithoutSaving()
    {
        using var connection = TestDatabases.OpenMemory();
        int id;
        OrderDto baseline;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            id = SeedOrder(seed);
            baseline = await ReadOrder(seed, PipelineOrderMapper.Create(seed.Model), id);
        }

        var draft = new OrderDto
        {
            Name = baseline.Name,
            Notes = baseline.Notes,
            Code = baseline.Code,
        };
        var session = draft.CreateEditSession();
        session.Observable.Notes = "local";
        var submitted = session.CreateChangeSet();
        using (var other = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            var otherOrder = await other.Orders.SingleAsync(order => order.Id == id);
            otherOrder.Notes = "remote";
            await other.SaveChangesAsync();
        }

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = PipelineOrderMapper.Create(context.Model);
        var conflictHookCalls = 0;
        var saveHookCalls = 0;
        var result = await mapper.PipelineExecuteAsync(
            context,
            submitted,
            order => order.Id == id,
            session,
            beforeSave: (_, _) =>
            {
                saveHookCalls++;
                return Task.CompletedTask;
            },
            onConflict: (rebase, _) =>
            {
                conflictHookCalls++;
                rebase.HasConflicts.ShouldBeTrue();
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntityPipelineStage.RebaseConflict);
        result.Rebase.Conflicts.ShouldNotBeEmpty();
        conflictHookCalls.ShouldBe(1);
        saveHookCalls.ShouldBe(0);
        session.HasChanges.ShouldBeTrue();
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        (await fresh.Orders.SingleAsync(order => order.Id == id)).Notes.ShouldBe("remote");
    }

    [Test]
    public async Task PipelineReturnsReconcileFailureAfterPersistence()
    {
        using var connection = TestDatabases.OpenMemory();
        int id;
        OrderDto baseline;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            id = SeedOrder(seed);
            baseline = await ReadOrder(seed, PipelineOrderMapper.Create(seed.Model), id);
        }

        var session = new RejectingPipelineSession();
        var changes = baseline.CreateChangeSet(
            new OrderDto
            {
                Name = baseline.Name,
                Notes = "saved",
                Code = baseline.Code,
            }
        );
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = PipelineOrderMapper.Create(context.Model);
        SparseEntityReconcileFailure<OrderDto, OrderDto.ChangeSet>? callbackFailure = null;

        var result = await mapper.PipelineExecuteAsync(
            context,
            changes,
            order => order.Id == id,
            session,
            onReconcileFailed: (failure, _) =>
            {
                callbackFailure = failure;
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntityPipelineStage.ReconcileFailed);
        result.ReadBack!.Notes.ShouldBe("saved");
        callbackFailure.ShouldNotBeNull();
        callbackFailure.Persisted.Notes.ShouldBe("saved");
        session.CallCount.ShouldBe(1);
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        (await fresh.Orders.SingleAsync(order => order.Id == id)).Notes.ShouldBe("saved");
    }

    [Test]
    public async Task PipelinePropagatesBeforeSaveCallbackExceptions()
    {
        using var connection = TestDatabases.OpenMemory();
        int id;
        OrderDto baseline;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            id = SeedOrder(seed);
            baseline = await ReadOrder(seed, PipelineOrderMapper.Create(seed.Model), id);
        }

        var changes = baseline.CreateChangeSet(
            new OrderDto
            {
                Name = baseline.Name,
                Notes = "unsaved",
                Code = baseline.Code,
            }
        );
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = PipelineOrderMapper.Create(context.Model);
        var expected = new InvalidOperationException("before-save failure");

        var actual = await Should.ThrowAsync<InvalidOperationException>(() =>
            mapper.PipelineExecuteAsync(context, changes, order => order.Id == id, beforeSave: (_, _) => Task.FromException(expected))
        );

        actual.ShouldBeSameAs(expected);
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        (await fresh.Orders.SingleAsync(order => order.Id == id)).Notes.ShouldBe("baseline");
    }

    private static int SeedOrder(SampleDbContext context)
    {
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "baseline",
                    Code = "ABC",
                    Lines = [],
                }
            )
            .Entity;
        context.SaveChanges();
        return entity.Id;
    }

    private sealed class RejectingPipelineSession : ISparseEditSessionPipeline<OrderDto, OrderDto.ChangeSet>
    {
        public int CallCount { get; private set; }

        public bool TryReconcileForPipeline(OrderDto.ChangeSet submitted, OrderDto persisted, out IReadOnlyList<SparseConflict>? conflicts)
        {
            CallCount++;
            conflicts = null;
            return false;
        }
    }
}

internal static class SparseEntityPipelineDocsSample
{
    public static async Task Run()
    {
        using var connection = TestDatabases.OpenMemory();
        using var db = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = db
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "ABC",
                    Lines = [],
                }
            )
            .Entity;
        await db.SaveChangesAsync();

        var orderId = entity.Id;
        var mapper = PipelineOrderMapper.Create(db.Model);
        var draft = mapper.ToDto(entity);
        var session = draft.CreateEditSession();
        session.Observable.Notes = "Updated";
        var incomingChanges = session.CreateChangeSet();
        // sample: efcore-mapper-pipeline-save
        var result = await mapper.PipelineExecuteAsync(
            db,
            incomingChanges,
            filter: order => order.Id == orderId,
            session: session,
            beforeSave: (save, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            onConflict: (rebase, _) =>
            {
                Console.WriteLine($"Rebase conflicts: {rebase.Conflicts.Count}");
                return Task.CompletedTask;
            },
            onReconcileFailed: (failure, _) =>
            {
                Console.WriteLine($"Session conflicts: {failure.Conflicts?.Count ?? 0}");
                return Task.CompletedTask;
            }
        );

        DocsCheck.Require(result.Stage == SparseEntityPipelineStage.Saved, "pipeline persists and reconciles");
        DocsCheck.Require(result.ReadBack?.Notes == "Updated", "result contains the authoritative DTO");
        // /sample
    }
}

internal static class DocsCheck
{
    public static void Require(bool condition, string capability)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failed: " + capability);
        }
    }
}
