using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Nested scope reconciliation over wide sibling workloads.</summary>
/// <remarks>
/// Foreign membership is answered by a lightweight filter over the
/// reference-equality all-attributed set instead of materializing the large
/// complement of each parent's members per reconciliation call.
/// </remarks>
public sealed class NestedScopeForeignFilterTests
{
    private const int CreatedPerBin = 8;

    private static BinHolderDto ReadBins(BinHolderEntity holder) =>
        new()
        {
            Bins = holder
                .Bins.Select(static bin => new BinDto
                {
                    Code = new BinCode(bin.Aisle, bin.Bay),
                    Lines = bin
                        .Lines.Select(static line => new BinLineDto
                        {
                            Id = line.Id,
                            Qty = line.Qty,
                        })
                        .ToList(),
                })
                .ToList(),
        };

    /// <summary>Builds the incoming DTO: created rows per bin, one identity shared by all sibling scopes.</summary>
    private static BinHolderDto BuildIncoming(
        BinHolderEntity entity,
        int binCount,
        Guid sharedTemporary
    )
    {
        var incoming = ReadBins(entity);
        for (var binIndex = 0; binIndex < binCount; binIndex++)
        {
            for (var lineIndex = 0; lineIndex < CreatedPerBin; lineIndex++)
            {
                var qty = binIndex * 1000 + lineIndex;
                incoming
                    .Bins[binIndex]
                    .Lines.Add(
                        new BinLineDto
                        {
                            Qty = qty,
                            TemporaryId = lineIndex == 0 ? sharedTemporary : Guid.NewGuid(),
                        }
                    );
            }
        }
        return incoming;
    }

    private static SparseEntityMapper<BinHolderEntity, BinHolderDto> CreateMapper(
        KeyedMappingContext context
    ) =>
        SparseEntityMapper<BinHolderEntity, BinHolderDto>.Create(
            new BinHolderMapper(),
            context.Model
        );

    private static async Task<BinHolderEntity> SeedAsync(KeyedMappingContext context, int binCount)
    {
        var entity = context
            .BinHolders.Add(
                new BinHolderEntity
                {
                    Bins = Enumerable
                        .Range(0, binCount)
                        .Select(static index => new BinEntity { Aisle = "aisle", Bay = index + 1 })
                        .ToList(),
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static holder => holder.Bins).LoadAsync();
        foreach (var bin in entity.Bins)
        {
            await context.Entry(bin).Collection(static bin => bin.Lines).LoadAsync();
        }

        return entity;
    }

    [Test]
    public async Task WideSiblingBinsReconcileEveryScopeIndependently()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        const int binCount = 40;
        var sharedTemporary = Guid.NewGuid();
        var entity = await SeedAsync(context, binCount);
        var mapper = CreateMapper(context);
        var baseline = ReadBins(entity);
        var incoming = BuildIncoming(entity, binCount, sharedTemporary);
        var accepted = baseline.CreateChangeSet(incoming);

        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // Typed scopes must not depend on collection position.
        var authoritative = ReadBins(entity);
        authoritative.Bins.Reverse();
        mapper.AssignTemporaryKeys(context, result, baseline, authoritative);
        counter.Commands.ShouldBe(0);

        var identities = new HashSet<int>();
        for (var binIndex = 0; binIndex < binCount; binIndex++)
        {
            var expectedBin = incoming.Bins[binIndex];
            var bay = binIndex + 1;
            var actualBin = authoritative.Bins.Single(bin => bin.Code.Bay == bay);
            actualBin.Lines.Count.ShouldBe(CreatedPerBin);
            foreach (var row in actualBin.Lines)
            {
                var expected = expectedBin.Lines.Single(line => line.Qty == row.Qty);
                row.TemporaryId.ShouldBe(expected.TemporaryId);
                identities.Add(row.Id!.Value);
            }
        }

        // Identical ids stay distinct across sibling scopes, including the
        // one temporary identity shared by every bin.
        identities.Count.ShouldBe(binCount * CreatedPerBin);
    }

    [Test]
    public async Task AssignTemporaryKeysAllocationScalesLinearlyWithParentCount()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var sharedTemporary = Guid.NewGuid();
        const int smallParents = 64;
        const int largeParents = 512;

        var assetMapper = CreateMapper(context);
        var smallResult = await ApplyWideSiblingsAsync(
            context,
            assetMapper,
            sharedTemporary,
            smallParents
        );
        var largeResult = await ApplyWideSiblingsAsync(
            context,
            assetMapper,
            sharedTemporary,
            largeParents
        );

        // Reconciling every parent must not materialize a foreign-member set
        // spanning all other parents: with P parents and K creations each,
        // the old complement built P*(P-1)*K references, roughly quadratic in
        // P. The lightweight filter stays linear, so allocated bytes grow by
        // the parent factor (~8x) rather than ~64x.
        var smallBytes = MeasureAssignBytes(context, assetMapper, smallResult);
        var largeBytes = MeasureAssignBytes(context, assetMapper, largeResult);
        (largeBytes / (double)smallBytes).ShouldBeLessThan(20);
    }

    private static async Task<(
        BinHolderEntity Entity,
        SparseEntityApplyResult Result,
        BinHolderDto Baseline
    )> ApplyWideSiblingsAsync(
        KeyedMappingContext context,
        SparseEntityMapper<BinHolderEntity, BinHolderDto> mapper,
        Guid sharedTemporary,
        int binCount
    )
    {
        var entity = await SeedAsync(context, binCount);
        var baseline = ReadBins(entity);
        var accepted = baseline.CreateChangeSet(BuildIncoming(entity, binCount, sharedTemporary));
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (entity, result, baseline);
    }

    private static long MeasureAssignBytes(
        KeyedMappingContext context,
        SparseEntityMapper<BinHolderEntity, BinHolderDto> mapper,
        (BinHolderEntity Entity, SparseEntityApplyResult Result, BinHolderDto Baseline) applied
    )
    {
        long measured = long.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var authoritative = ReadBins(applied.Entity);
            authoritative.Bins.Reverse();
            _ = GC.GetTotalAllocatedBytes(precise: true);
            var baseline = GC.GetTotalAllocatedBytes(precise: true);
            mapper.AssignTemporaryKeys(context, applied.Result, applied.Baseline, authoritative);
            var after = GC.GetTotalAllocatedBytes(precise: true);
            measured = Math.Min(measured, after - baseline);
        }

        return measured;
    }
}
