using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class SoAddressEntity
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

public sealed class SoTagEntity
{
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

public sealed class SoCustomerEntity
{
    public int Id { get; set; }

    public SoAddressEntity? Billing { get; set; }

    public SoAddressEntity? Shipping { get; set; }

    public List<SoTagEntity> PublicTags { get; set; } = [];

    public List<SoTagEntity> PrivateTags { get; set; } = [];
}

public sealed class SoContext(DbContextOptions<SoContext> options) : DbContext(options)
{
    public DbSet<SoCustomerEntity> Customers => Set<SoCustomerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SoCustomerEntity>(entity =>
        {
            entity.HasKey(customer => customer.Id);
            entity.OwnsOne(customer => customer.Billing);
            entity.OwnsOne(customer => customer.Shipping);
            entity.OwnsMany(customer => customer.PublicTags, tags => tags.HasKey(tag => tag.Code));
            entity.OwnsMany(customer => customer.PrivateTags, tags => tags.HasKey(tag => tag.Code));
        });
    }
}

[SparseModel]
public partial class SoAddressDto
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

[SparseModel]
public partial class SoTagDto
{
    [SparseKey]
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

[SparseModel]
public partial class SoCustomerDto
{
    public SoAddressDto? Billing { get; set; }

    public SoAddressDto? Shipping { get; set; }

    public List<SoTagDto> PublicTags { get; set; } = [];

    public List<SoTagDto> PrivateTags { get; set; } = [];
}

[SparseFragmentMapper<SoCustomerEntity, SoCustomerDto>]
public sealed partial class SoCustomerMapper
{
    private void Configure()
    {
        Map.Object(dto => dto.Billing, entity => entity.Billing, _ => { });
        Map.Object(dto => dto.Shipping, entity => entity.Shipping, _ => { });
        Map.Collection(dto => dto.PublicTags, entity => entity.PublicTags, tags =>
        {
            tags.Key(dto => dto.Code, entity => entity.Code);
            tags.Delete();
        });
        Map.Collection(dto => dto.PrivateTags, entity => entity.PrivateTags, tags =>
        {
            tags.Key(dto => dto.Code, entity => entity.Code);
            tags.Delete();
        });
    }
}

/// <summary>One owned CLR type mapped under several navigations has one EF entity type per navigation.</summary>
public sealed class SharedOwnedClrTypeTests
{
    private static SoContext CreateContext(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var context = new SoContext(new DbContextOptionsBuilder<SoContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        return context;
    }

    [Test]
    public void SharedClrTypeHasNoSingleGlobalEntityType()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = CreateContext(connection);

        context.Model.GetEntityTypes().Count(type => type.ClrType == typeof(SoAddressEntity)).ShouldBe(2);
        context.Model.GetEntityTypes().Count(type => type.ClrType == typeof(SoTagEntity)).ShouldBe(2);
    }

    [Test]
    public async Task ObjectsAndCollectionsOverSharedClrTypeBuildAndApply()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = CreateContext(connection);
        var entity = context
            .Customers.Add(
                new SoCustomerEntity
                {
                    Billing = new SoAddressEntity { Street = "B1", City = "Osaka" },
                    Shipping = new SoAddressEntity { Street = "S1", City = "Kyoto" },
                    PublicTags = [new SoTagEntity { Code = "a", Label = "A" }],
                    PrivateTags = [new SoTagEntity { Code = "a", Label = "PA" }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        context.Entry(entity).Collection(customer => customer.PublicTags).IsLoaded = true;
        context.Entry(entity).Collection(customer => customer.PrivateTags).IsLoaded = true;

        var mapper = SparseEntityMapper<SoCustomerEntity, SoCustomerDto>.Create(new SoCustomerMapper(), context.Model);
        var current = mapper.ToDto(entity);
        current.Billing!.Street.ShouldBe("B1");
        current.Shipping!.City.ShouldBe("Kyoto");
        current.PublicTags.Single().Label.ShouldBe("A");
        current.PrivateTags.Single().Label.ShouldBe("PA");
        var incoming = mapper.ToDto(entity);
        incoming.Billing!.Street = "B2";
        incoming.Shipping!.City = "Nara";
        incoming.PrivateTags[0].Label = "PB";

        mapper.Apply(context, entity, current.CreateChangeSet(incoming));

        entity.Billing!.Street.ShouldBe("B2");
        entity.Billing.City.ShouldBe("Osaka");
        entity.Shipping!.City.ShouldBe("Nara");
        entity.Shipping.Street.ShouldBe("S1");
        entity.PublicTags.Select(tag => tag.Label).ShouldBe(["A"]);
        entity.PrivateTags.Select(tag => tag.Code).ShouldBe(["a"]);
        entity.PrivateTags.Single(tag => tag.Code == "a").Label.ShouldBe("PB");
        await context.SaveChangesAsync();
    }
}
