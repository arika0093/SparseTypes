using Microsoft.EntityFrameworkCore;
using SparseTypes.Playground;

namespace SparseTypes.Playground.Pages.Concurrency;

internal static class ConcurrencySimulation
{
    internal static void SimulateConcurrentEdit(
        DbContextOptions<ConcurrentOrderContext> options,
        int orderId
    )
    {
        using var otherUserDb = new ConcurrentOrderContext(options);
        var otherUserOrder = otherUserDb.Orders.Single(order => order.Id == orderId);
        otherUserOrder.CustomerName = PlaygroundRandom.CreateDifferentCustomerName(
            otherUserOrder.CustomerName
        );
        otherUserOrder.Revision++;
        otherUserDb.SaveChanges();
    }

    internal static ConcurrentUpdate SimulatePeriodicUpdate(
        DbContextOptions<ConcurrentOrderContext> options,
        int orderId
    )
    {
        using var otherUserDb = new ConcurrentOrderContext(options);
        var order = otherUserDb
            .Orders.Include(static item => item.Note)
            .Single(item => item.Id == orderId);
        var field =
            Random.Shared.Next(2) == 0 ? ConcurrentOrderField.Customer : ConcurrentOrderField.Notes;
        var before =
            field == ConcurrentOrderField.Customer
                ? order.CustomerName
                : order.Note?.Text ?? string.Empty;
        var after =
            field == ConcurrentOrderField.Customer
                ? PlaygroundRandom.CreateDifferentCustomerName(order.CustomerName)
                : $"Concurrent note {order.Revision + 1}";
        while (string.Equals(before, after, StringComparison.Ordinal))
        {
            after += "*";
        }

        SetValue(order, field, after);
        order.Revision++;
        otherUserDb.SaveChanges();
        return new ConcurrentUpdate
        {
            Property = field == ConcurrentOrderField.Customer ? "Customer" : "Notes",
            Before = before,
            After = after,
            Revision = order.Revision,
        };
    }

    internal static void EnsureCurrentRevision(
        DbContextOptions<ConcurrentOrderContext> options,
        int orderId,
        int expectedRevision
    )
    {
        using var currentDb = new ConcurrentOrderContext(options);
        var currentRevision = currentDb
            .Orders.Where(order => order.Id == orderId)
            .Select(order => order.Revision)
            .Single();
        if (currentRevision != expectedRevision)
        {
            throw new DbUpdateConcurrencyException(
                $"Order {orderId} changed from revision {expectedRevision} to {currentRevision}."
            );
        }
    }

    private static void SetValue(
        ConcurrentOrderEntity order,
        ConcurrentOrderField field,
        string value
    )
    {
        if (field == ConcurrentOrderField.Customer)
        {
            order.CustomerName = value;
            return;
        }

        if (order.Note is null)
        {
            order.Note = new ConcurrentNoteEntity();
        }

        order.Note.Text = value;
    }
}

internal enum ConcurrentOrderField
{
    Customer,
    Notes,
}

internal sealed class ConcurrentUpdate
{
    public required string Property { get; init; }

    public required string Before { get; init; }

    public required string After { get; init; }

    public required int Revision { get; init; }
}
