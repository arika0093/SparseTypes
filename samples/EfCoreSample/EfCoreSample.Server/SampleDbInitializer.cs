namespace EfCoreSample.Server;

internal static class SampleDbInitializer
{
    public static void Initialize(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        db.Database.EnsureCreated();
        if (db.Orders.Any())
        {
            return;
        }

        db.Orders.Add(
            new OrderEntity
            {
                CustomerName = "Ann",
                Notes = "keep",
                Code = "abc",
                Address = new AddressEntity { Street = "Old Street", City = "Osaka" },
                Lines =
                [
                    new OrderLineEntity
                    {
                        Quantity = 1,
                        UnitPrice = 10m,
                        SortOrder = 0,
                    },
                    new OrderLineEntity
                    {
                        Quantity = 2,
                        UnitPrice = 20m,
                        SortOrder = 1,
                    },
                ],
            }
        );
        db.SaveChanges();
    }
}
