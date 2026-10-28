using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class EqualSoftOrderEntity
{
    public int Id { get; set; }
    public List<EqualSoftLineEntity> Lines { get; set; } = [];
}

public sealed class EqualSoftLineEntity
{
    public int Id { get; set; }
    public int Qty { get; set; }
    public bool IsDeleted { get; set; }
    public int Position { get; set; }
    public int OrderId { get; set; }

    public override bool Equals(object? obj) => obj is EqualSoftLineEntity;

    public override int GetHashCode() => 0;
}

public sealed class EqualSoftContext(DbContextOptions<EqualSoftContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EqualSoftOrderEntity>().HasMany(order => order.Lines).WithOne().HasForeignKey(line => line.OrderId);
}

[SparseFragmentMapper<EqualSoftOrderEntity, SoftOrderDto>]
public sealed partial class EqualSoftMapping
{
    private void Configure() =>
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Key(dto => dto.Key, entity => entity.Id);
                lines.Element(child =>
                {
                    child.Ignore(dto => dto.Id);
                    child.Ignore(dto => dto.Key);
                    child.Member(dto => dto.Qty, entity => entity.Qty);
                });
                lines.SoftDelete(entity => entity.IsDeleted = true, entity => entity.IsDeleted);
                lines.Order(entity => entity.Position);
            }
        );
}

public sealed class SoftDeleteIdentityRetentionTests
{
    [Test]
    public async Task ValueEqualTrackedEntitiesRemainDistinctAndKeepTrackedTailOrder()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = new EqualSoftContext(new DbContextOptionsBuilder<EqualSoftContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var root = new EqualSoftOrderEntity
        {
            Lines =
            [
                new EqualSoftLineEntity
                {
                    Id = 4,
                    Qty = 4,
                    IsDeleted = true,
                },
                new EqualSoftLineEntity { Id = 2, Qty = 2 },
                new EqualSoftLineEntity
                {
                    Id = 3,
                    Qty = 3,
                    IsDeleted = true,
                },
                new EqualSoftLineEntity { Id = 1, Qty = 1 },
            ],
        };
        context.Add(root);
        await context.SaveChangesAsync();
        context.Entry(root).Collection(order => order.Lines).IsLoaded = true;
        var original = root.Lines.ToDictionary(static line => line.Id);
        var before = new SoftOrderDto { Lines = [new SoftLineDto { Id = 1, Qty = 1 }, new SoftLineDto { Id = 2, Qty = 2 }] };
        var after = new SoftOrderDto { Lines = [new SoftLineDto { Id = 1, Qty = 1 }] };
        var mapper = SparseEntityMapper<EqualSoftOrderEntity, SoftOrderDto>.Create(new EqualSoftMapping(), context.Model);

        mapper.Apply(context, root, before.CreateChangeSet(after));

        root.Lines.Select(static line => line.Id).ShouldBe([1, 2, 4, 3]);
        root.Lines.Select(static line => line.Position).ShouldBe([0, 1, 2, 3]);
        foreach (var line in root.Lines)
        {
            line.ShouldBeSameAs(original[line.Id]);
        }
        root.Lines.Where(static line => line.IsDeleted).Select(static line => line.Id).ShouldBe([2, 4, 3]);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.Set<EqualSoftLineEntity>().OrderBy(line => line.Position).ToListAsync();
        persisted.Select(static line => line.Id).ShouldBe([1, 2, 4, 3]);
    }
}
