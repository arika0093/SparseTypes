using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore.Tests;

[SparseModel]
public partial class ConstructorValidationDto
{
    public string Label { get; set; } = string.Empty;
    public ConstructorValidationChildDto? First { get; set; }
    public ConstructorValidationChildDto? Second { get; set; }
}

public partial class ConstructorValidationChildDto
{
    public string Value { get; set; } = string.Empty;
}

public sealed class ConstructorValidationEntity
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int? FirstId { get; set; }
    public ConstructorValidationChildEntity? First { get; set; }
    public int? SecondId { get; set; }
    public ConstructorValidationChildEntity? Second { get; set; }
}

public sealed class ConstructorValidationChildEntity
{
    public ConstructorValidationChildEntity() => throw new InvalidOperationException("sensitive constructor detail");

    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class ConstructorValidationContext(DbContextOptions<ConstructorValidationContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConstructorValidationEntity>().HasOne(entity => entity.First).WithMany().HasForeignKey(entity => entity.FirstId);
        modelBuilder.Entity<ConstructorValidationEntity>().HasOne(entity => entity.Second).WithMany().HasForeignKey(entity => entity.SecondId);
    }
}

[SparseFragmentMapper<ConstructorValidationEntity, ConstructorValidationDto>]
public sealed partial class ConstructorValidationMapping
{
    private void Configure()
    {
        Map.Member(dto => dto.Label, entity => entity.Label);
        Map.Object(dto => dto.First, entity => entity.First, child => child.Member(dto => dto.Value, entity => entity.Value));
        Map.Object(dto => dto.Second, entity => entity.Second, child => child.Member(dto => dto.Value, entity => entity.Value));
    }
}

public sealed class ProspectiveConstructorValidationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ConstructorFailuresAreCollectedWithoutWritingOrAttaching(bool wholesale)
    {
        using var context = new ConstructorValidationContext(
            new DbContextOptionsBuilder<ConstructorValidationContext>().UseSqlite("Data Source=:memory:").Options
        );
        var entity = new ConstructorValidationEntity { Id = 1, Label = "before" };
        context.Entry(entity).Reference(root => root.First).IsLoaded = true;
        context.Entry(entity).Reference(root => root.Second).IsLoaded = true;
        var before = new ConstructorValidationDto
        {
            Label = "before",
            First = wholesale ? null : new ConstructorValidationChildDto { Value = "before" },
            Second = wholesale ? null : new ConstructorValidationChildDto { Value = "before" },
        };
        var after = new ConstructorValidationDto
        {
            Label = "after",
            First = new ConstructorValidationChildDto { Value = "after" },
            Second = new ConstructorValidationChildDto { Value = "after" },
        };
        var mapper = SparseEntityMapper<ConstructorValidationEntity, ConstructorValidationDto>.Create(
            new ConstructorValidationMapping(),
            context.Model
        );
        var failure = Should.Throw<SparseEntityMappingException>(() => mapper.Apply(context, entity, before.CreateChangeSet(after)));
        failure.Errors.Count.ShouldBe(2);
        failure.Errors.ShouldAllBe(error => error.Failure == SparseEntityMappingFailure.NestedNotCreatable);
        failure.Errors.Select(static error => error.DtoPath).Order().ShouldBe(["First", "Second"]);
        failure.Message.ShouldNotContain("sensitive constructor detail");
        entity.Label.ShouldBe("before");
        entity.First.ShouldBeNull();
        entity.Second.ShouldBeNull();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Test]
    public void ConstructorCancellationIsNotCollectedAsInvalidInput()
    {
        var cancellation = new OperationCanceledException();
        Func<object> factory = () => throw new TargetInvocationException(cancellation);
        var failure = Should.Throw<TargetInvocationException>(() => ProspectiveEntityFactory.Create(factory, "First", "First"));
        failure.InnerException.ShouldBeSameAs(cancellation);
    }
}
