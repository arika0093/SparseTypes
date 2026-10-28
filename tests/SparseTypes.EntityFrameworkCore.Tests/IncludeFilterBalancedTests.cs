using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

// Guid-keyed and string-keyed collections with explicit key selectors; the
// balanced predicate builder is shared between both key types.
public partial class FilterTokenDto
{
    /// <summary>Gets or sets the identity token.</summary>
    [SparseKey]
    public Guid Signature { get; set; }

    /// <summary>Gets or sets the amount.</summary>
    public int Amount { get; set; }
}

/// <summary>String-keyed label element.</summary>
public partial class FilterLabelDto
{
    /// <summary>Gets or sets the stable label code.</summary>
    [SparseKey]
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the amount.</summary>
    public int Amount { get; set; }
}

/// <summary>DTO holding both keyed collections.</summary>
[SparseModel]
public partial class FilterTokenBoxDto
{
    /// <summary>Gets or sets the Guid-keyed tokens.</summary>
    public List<FilterTokenDto> Tokens { get; set; } = [];

    /// <summary>Gets or sets the string-keyed labels.</summary>
    public List<FilterLabelDto> Labels { get; set; } = [];
}

/// <summary>Token entity identified by its Guid key.</summary>
public sealed class FilterTokenEntity
{
    /// <summary>Gets or sets the identity token.</summary>
    public Guid Signature { get; set; }

    /// <summary>Gets or sets the amount.</summary>
    public int Amount { get; set; }

    /// <summary>Gets or sets the owning box identity.</summary>
    public int BoxId { get; set; }

    /// <summary>Gets or sets the owning box.</summary>
    public FilterTokenBoxEntity? Box { get; set; }
}

/// <summary>Label entity identified by its string key.</summary>
public sealed class FilterLabelEntity
{
    /// <summary>Gets or sets the stable label code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the amount.</summary>
    public int Amount { get; set; }

    /// <summary>Gets or sets the owning box identity.</summary>
    public int BoxId { get; set; }

    /// <summary>Gets or sets the owning box.</summary>
    public FilterTokenBoxEntity? Box { get; set; }
}

/// <summary>Box entity holding both keyed collections.</summary>
public sealed class FilterTokenBoxEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the tokens.</summary>
    public List<FilterTokenEntity> Tokens { get; set; } = [];

    /// <summary>Gets or sets the labels.</summary>
    public List<FilterLabelEntity> Labels { get; set; } = [];
}

/// <summary>SQLite in-memory context for the filter models.</summary>
public sealed class FilterTokenContext : DbContext
{
    /// <summary>Creates the filter context.</summary>
    public FilterTokenContext(DbContextOptions<FilterTokenContext> options)
        : base(options) { }

    /// <summary>Gets the token boxes.</summary>
    public DbSet<FilterTokenBoxEntity> Boxes => Set<FilterTokenBoxEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilterTokenBoxEntity>(box =>
        {
            box.HasKey(entity => entity.Id);
            box.HasMany(entity => entity.Tokens)
                .WithOne(token => token.Box)
                .HasForeignKey(token => token.BoxId)
                .OnDelete(DeleteBehavior.Cascade);
            box.HasMany(entity => entity.Labels)
                .WithOne(label => label.Box)
                .HasForeignKey(label => label.BoxId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FilterTokenEntity>().HasKey(token => token.Signature);
        modelBuilder.Entity<FilterLabelEntity>().HasKey(label => label.Code);
    }
}

/// <summary>Keyed collections built through an explicit mapper.</summary>
[SparseFragmentMapper<FilterTokenBoxEntity, FilterTokenBoxDto>]
public sealed partial class FilterTokenBoxMapper
{
    private void Configure()
    {
        Map.Collection(
            dto => dto.Tokens,
            entity => entity.Tokens,
            tokens =>
            {
                tokens.Key(dto => dto.Signature, entity => entity.Signature);
                tokens.Delete();
            }
        );
        Map.Collection(
            dto => dto.Labels,
            entity => entity.Labels,
            labels =>
            {
                labels.Key(dto => dto.Code, entity => entity.Code);
                labels.Delete();
            }
        );
    }
}

/// <summary>Filtered-Include predicate shape for wide edit batches.</summary>
/// <remarks>
/// Balanced OR trees replace the left-deep spine: depth grows logarithmically
/// while the equality leaf set and per-leaf semantics stay identical.
/// </remarks>
public sealed class IncludeFilterBalancedTests
{
    /// <summary>Collects OR depth and equality leaf constants iteratively.</summary>
    private static (int MaxDepth, List<object> Leaves) InspectOrChain(Expression root)
    {
        var maxDepth = 0;
        var leaves = new List<object>();
        var stack = new Stack<(Expression Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (node is BinaryExpression { NodeType: ExpressionType.OrElse } orElse)
            {
                maxDepth = Math.Max(maxDepth, depth + 1);
                stack.Push((orElse.Right, depth + 1));
                stack.Push((orElse.Left, depth + 1));
            }
            else if (node is BinaryExpression { NodeType: ExpressionType.Equal } equal)
            {
                leaves.Add(ExtractConstant(equal.Right)!);
            }
            else
            {
                foreach (var child in Children(node))
                {
                    stack.Push((child, depth));
                }
            }
        }

        return (maxDepth, leaves);
    }

    private static (int MaxDepth, List<object> Leaves) InspectFilterPredicate<T>(
        IQueryable<T> query,
        Type elementType
    )
        where T : class
    {
        var predicate = FindWhere(query.Expression, elementType);
        predicate.ShouldNotBeNull();
        return InspectOrChain(predicate.Body);
    }

    private static LambdaExpression? FindWhere(Expression root, Type elementSelectorType)
    {
        LambdaExpression? where = null;
        var stack = new Stack<Expression>();
        stack.Push(root);
        while (stack.Count > 0 && where is null)
        {
            var node = stack.Pop();
            if (
                node is MethodCallExpression call
                && call.Method.DeclaringType == typeof(Enumerable)
                && call.Method.Name == nameof(Enumerable.Where)
                && call.Arguments.Count == 2
                && call.Arguments[1] is LambdaExpression lambda
                && lambda.Parameters[0].Type == elementSelectorType
            )
            {
                where = lambda;
                break;
            }

            foreach (var child in Children(node))
            {
                stack.Push(child);
            }
        }

        return where;
    }

    private static object? ExtractConstant(Expression value)
    {
        while (
            value is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked
        )
        {
            value = unary.Operand;
        }

        return value is ConstantExpression constant ? constant.Value : null;
    }

    private static IEnumerable<Expression> Children(Expression node)
    {
        switch (node)
        {
            case BinaryExpression binary:
                yield return binary.Left;
                yield return binary.Right;
                break;
            case UnaryExpression unary:
                yield return unary.Operand;
                break;
            case LambdaExpression lambda:
                yield return lambda.Body;
                break;
            case MemberExpression member:
                if (member.Expression is not null)
                {
                    yield return member.Expression;
                }
                break;
            case MethodCallExpression call:
                if (call.Object is not null)
                {
                    yield return call.Object;
                }
                foreach (var argument in call.Arguments)
                {
                    yield return argument;
                }
                break;
            case MemberInitExpression memberInit:
                yield return memberInit.NewExpression;
                break;
        }
    }

    private static int BalancedDepth(int leaves)
    {
        var depth = 0;
        var count = leaves;
        while (count > 1)
        {
            count = (count + 1) / 2;
            depth++;
        }

        return depth;
    }

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

    private static async Task<(OrderEntity Entity, SampleDbContext Context)> SeedSampleAsync(
        SqliteConnection connection,
        int lineCount
    )
    {
        var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "ABC",
                    Lines = Enumerable
                        .Range(0, lineCount)
                        .Select(static index => new OrderLineEntity
                        {
                            Quantity = index,
                            UnitPrice = 2m,
                        })
                        .ToList(),
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        return (entity, context);
    }

    private static ISparseChangeSet<OrderDto> EditBatch(OrderEntity entity, int editedCount)
    {
        var current = ReadOrder(entity);
        var incoming = ReadOrder(entity);
        // Non-contiguous ids guard the deduplication and pairing paths.
        for (var index = 0; index < editedCount; index++)
        {
            incoming.Lines[index * 2].Quantity += 1000;
        }
        return current.CreateChangeSet(incoming);
    }

    private static SparseEntityMapper<OrderEntity, OrderDto> SampleMapper(
        SampleDbContext context
    ) =>
        SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new ProjectionOrderMapper(),
            context.Model
        );

    [Test]
    public async Task WideEditBatchBuildsBalancedOrElseTree()
    {
        using var connection = TestDatabases.OpenMemory();
        var (entity, context) = await SeedSampleAsync(connection, 2000);
        var mapper = SampleMapper(context);
        var changes = EditBatch(entity, 1000);

        var query = context
            .Orders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var (depth, leaves) = InspectFilterPredicate(query, typeof(OrderLineEntity));

        depth.ShouldBe(BalancedDepth(leaves.Count));
        leaves.Count.ShouldBe(1000);
        depth.ShouldBeLessThanOrEqualTo(20);
        // The queried shape stays translatable for a supported provider.
        _ = query.ToQueryString();
    }

    [Test]
    public async Task WideEditBatchKeepsLeafOrderAndSemantics()
    {
        using var connection = TestDatabases.OpenMemory();
        var (entity, context) = await SeedSampleAsync(connection, 2000);
        var mapper = SampleMapper(context);
        var changes = EditBatch(entity, 1000);
        var expectedIds = Enumerable
            .Range(0, 1000)
            .Select(index => entity.Lines[index * 2].Id)
            .ToList();

        var query = context
            .Orders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var (depth, leaves) = InspectFilterPredicate(query, typeof(OrderLineEntity));

        depth.ShouldBe(BalancedDepth(leaves.Count));
        // Any change to the key set or its order is mirrored in the tree.
        leaves.Select(static constant => (int)constant!).ShouldBe(expectedIds);

        // Exercised through EF, the predicate is translated and matches exactly
        // those lines (EF.Property selectors must stay inside a provider query).
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        var predicate = FindWhere(query.Expression, typeof(OrderLineEntity))!;
        var compiled = Expression.Lambda<Func<OrderLineEntity, bool>>(
            predicate.Body,
            predicate.Parameters[0]
        );
        var matched = await fresh
            .Set<OrderLineEntity>()
            .AsNoTracking()
            .Where(compiled)
            .Select(static line => line.Id)
            .ToListAsync();
        matched.ShouldBe(expectedIds);
    }

    [Test]
    public async Task SingleEditedKeyBuildsNoOrNode()
    {
        using var connection = TestDatabases.OpenMemory();
        var (entity, context) = await SeedSampleAsync(connection, 4);
        var mapper = SampleMapper(context);
        var changes = EditBatch(entity, 1);

        var query = context
            .Orders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var (depth, leaves) = InspectFilterPredicate(query, typeof(OrderLineEntity));

        depth.ShouldBe(0);
        leaves.ShouldHaveSingleItem();
        _ = query.ToQueryString();
    }

    [Test]
    public async Task OversizedEditBatchLoadsUnfilteredAndAppliesOnlyAddressedChildren()
    {
        using var connection = TestDatabases.OpenMemory();
        var editedCount = SparseTypes.EntityFrameworkCore.Mapping.IncludePathCollector.MaximumFilterKeys + 1;
        var seeded = await SeedSampleAsync(connection, editedCount * 2);
        using var context = seeded.Context;
        var mapper = SampleMapper(context);
        var changes = EditBatch(seeded.Entity, editedCount);
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        var query = fresh.Orders.Where(order => order.Id == seeded.Entity.Id).IncludeFor(mapper, changes);
        FindWhere(query.Expression, typeof(OrderLineEntity)).ShouldBeNull();
        _ = query.ToQueryString();
        var loaded = await query.SingleAsync();
        loaded.Lines.Count.ShouldBe(editedCount * 2);
        mapper.Apply(fresh, loaded, changes);
        var originalIndexes = seeded
            .Entity.Lines.Select((line, index) => (line.Id, Index: index))
            .ToDictionary(static item => item.Id, static item => item.Index);
        foreach (var line in loaded.Lines)
        {
            var originalIndex = originalIndexes[line.Id];
            line.Quantity.ShouldBe(originalIndex % 2 == 0 ? originalIndex + 1000 : originalIndex);
        }
    }

    [Test]
    public async Task FilteredIncludeLoadsOnlyAddressedChildren()
    {
        using var connection = TestDatabases.OpenMemory();
        var (entity, context) = await SeedSampleAsync(connection, 18);
        var mapper = SampleMapper(context);
        var changes = EditBatch(entity, 7);
        var editedIdentities = Enumerable
            .Range(0, 7)
            .Select(index => entity.Lines[index * 2].Id)
            .ToList();

        // A fresh context prevents tracked-entity fixup from resurrecting
        // rows the filtered include did not address.
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        var loaded = await fresh
            .Orders.IncludeFor(mapper, changes)
            .Where(order => order.Id == entity.Id)
            .SingleAsync();

        loaded.Lines.Select(static line => line.Id).ToList().ShouldBe(editedIdentities);
        mapper.Apply(fresh, loaded, changes);
        loaded.Lines.Count.ShouldBe(editedIdentities.Count);
        loaded.Lines.ShouldAllBe(static line => line.Quantity >= 1000);
    }

    [Test]
    public async Task StringKeysAndGuidKeysStayFiltered()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = new FilterTokenContext(
            new DbContextOptionsBuilder<FilterTokenContext>().UseSqlite(connection).Options
        );
        context.Database.EnsureCreated();
        var box = context
            .Boxes.Add(
                new FilterTokenBoxEntity
                {
                    Tokens = Enumerable
                        .Range(0, 10)
                        .Select(static index => new FilterTokenEntity
                        {
                            Signature = Guid.NewGuid(),
                            Amount = index,
                        })
                        .ToList(),
                    Labels = Enumerable
                        .Range(0, 20)
                        .Select(static index => new FilterLabelEntity
                        {
                            Code = $"code-{index:D2}",
                            Amount = index,
                        })
                        .ToList(),
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(box).Collection(static box => box.Tokens).LoadAsync();
        await context.Entry(box).Collection(static box => box.Labels).LoadAsync();
        var trackedBox = await context.Boxes.SingleAsync();
        var mapper = SparseEntityMapper<FilterTokenBoxEntity, FilterTokenBoxDto>.Create(
            new FilterTokenBoxMapper(),
            context.Model
        );

        // Guid-keyed collection filtered on the entity key.
        var currentTokens = new FilterTokenBoxDto
        {
            Tokens = trackedBox
                .Tokens.Select(static token => new FilterTokenDto
                {
                    Signature = token.Signature,
                    Amount = token.Amount,
                })
                .ToList(),
            Labels = [],
        };
        var incomingTokens = new FilterTokenBoxDto
        {
            Tokens = currentTokens
                .Tokens.Select(static token => new FilterTokenDto
                {
                    Signature = token.Signature,
                    Amount = token.Amount,
                })
                .ToList(),
            Labels = [],
        };
        for (var index = 0; index < 6; index++)
        {
            incomingTokens.Tokens[index].Amount += 100;
        }
        var tokenChanges = currentTokens.CreateChangeSet(incomingTokens);

        // A fresh context prevents the seeded, already-loaded navigations from
        // short-circuiting the filtered-include materialization.
        var queryOptions = new DbContextOptionsBuilder<FilterTokenContext>()
            .UseSqlite(connection)
            .Options;
        using var queryContext = new FilterTokenContext(queryOptions);

        var tokenQuery = queryContext.Boxes.IncludeFor(mapper, tokenChanges);
        var (tokenDepth, tokenLeaves) = InspectFilterPredicate(
            tokenQuery,
            typeof(FilterTokenEntity)
        );
        tokenDepth.ShouldBe(BalancedDepth(tokenLeaves.Count));
        tokenLeaves.Select(static constant => (Guid)constant!).Count().ShouldBe(6);
        var tokenLoaded = await tokenQuery.Where(entity => entity.Id == box.Id).SingleAsync();
        tokenLoaded.Tokens.Count.ShouldBe(6);
        mapper.Apply(queryContext, tokenLoaded, tokenChanges);
        tokenLoaded.Tokens.ShouldAllBe(static token => token.Amount >= 100);

        // String-keyed collection filtered on the entity key.
        var currentLabels = new FilterTokenBoxDto
        {
            Tokens = [],
            Labels = trackedBox
                .Labels.Select(static label => new FilterLabelDto
                {
                    Code = label.Code,
                    Amount = label.Amount,
                })
                .ToList(),
        };
        var incomingLabels = new FilterTokenBoxDto
        {
            Tokens = [],
            Labels = currentLabels
                .Labels.Select(static label => new FilterLabelDto
                {
                    Code = label.Code,
                    Amount = label.Amount,
                })
                .ToList(),
        };
        for (var index = 0; index < 12; index++)
        {
            incomingLabels.Labels[index].Amount += 100;
        }
        var labelChanges = currentLabels.CreateChangeSet(incomingLabels);

        var labelQuery = queryContext.Boxes.IncludeFor(mapper, labelChanges);
        var (labelDepth, labelLeaves) = InspectFilterPredicate(
            labelQuery,
            typeof(FilterLabelEntity)
        );
        labelDepth.ShouldBe(BalancedDepth(labelLeaves.Count));
        labelLeaves.Select(static constant => (string)constant!).Count().ShouldBe(12);
        var labelLoaded = await labelQuery.Where(entity => entity.Id == box.Id).SingleAsync();
        labelLoaded.Labels.Count.ShouldBe(12);
        labelLoaded
            .Labels.Select(static label => label.Code)
            .ShouldBe(incomingLabels.Labels.Take(12).Select(static label => label.Code).ToList());
    }
}
