using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SparseTypes;
using SparseTypes.EntityFrameworkCore;

namespace SparseTypes.Playground.Pages.Concurrency;

/// <summary>Order DTO for concurrent editing. Knows nothing about EF Core.</summary>
[SparseModel]
public partial class ConcurrentOrderDto
{
    /// <summary>Gets or sets the customer. Renamed from the entity member.</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes. Same-name scalar mapping.</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Separate relational note entity (1:1 with the order).</summary>
public sealed class ConcurrentNoteEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the note text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public ConcurrentOrderEntity? Order { get; set; }
}

/// <summary>Tracked order entity. Carries no mapping attributes.</summary>
public sealed class ConcurrentOrderEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the optimistic concurrency revision.</summary>
    public int Revision { get; set; }

    /// <summary>Gets or sets the customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the separate note.</summary>
    public ConcurrentNoteEntity? Note { get; set; }
}

/// <summary>In-memory document context for concurrent editing.</summary>
public sealed class ConcurrentOrderContext : DbContext
{
    /// <summary>Creates a concurrent-editing context.</summary>
    /// <param name="options">The context options.</param>
    public ConcurrentOrderContext(DbContextOptions<ConcurrentOrderContext> options)
        : base(options) { }

    /// <summary>Gets the orders.</summary>
    public DbSet<ConcurrentOrderEntity> Orders => Set<ConcurrentOrderEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConcurrentOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Revision).IsConcurrencyToken();
            entity
                .HasOne(order => order.Note)
                .WithOne(note => note.Order)
                .HasForeignKey<ConcurrentNoteEntity>(note => note.OrderId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Maps order DTO changes onto the tracked order entity.</summary>
[SparseFragmentMapper<ConcurrentOrderEntity, ConcurrentOrderDto>]
public sealed partial class ConcurrentOrderMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Customer, entity => entity.CustomerName);
        Map.Member(dto => dto.Notes, entity => entity.Note!.Text);
        Map.Read(dto => dto.Notes, entity => entity.Note == null ? string.Empty : entity.Note.Text);
    }
}
