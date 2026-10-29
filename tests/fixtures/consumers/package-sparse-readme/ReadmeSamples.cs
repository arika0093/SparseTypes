using SparseTypes;

internal static class ReadmeSamples
{
    public static void Run()
    {
        // sample: readme-quickstart
        var order = new Order { CustomerName = "Alice", Quantity = 1 };
        var session = order.CreateEditSession();

        // Edit through the observable view; the original model is updated too.
        session.Observable.CustomerName = "Bob";
        Console.WriteLine(order.CustomerName); // Bob
        Console.WriteLine(session.HasChanges); // True
        DocsCheck.Require(order.CustomerName == "Bob" && session.HasChanges, "observable edits update the model and dirty state");

        // Inspect the difference from the start of the session.
        var changes = session.CreateChangeSet();
        Console.WriteLine(changes.CustomerName.Before.Value); // Alice
        Console.WriteLine(changes.CustomerName.After.Value);  // Bob
        Console.WriteLine(changes.Quantity.IsChanged);        // False
        DocsCheck.Require(
            changes.CustomerName.Before.Value == "Alice" && changes.CustomerName.After.Value == "Bob" && !changes.Quantity.IsChanged,
            "typed transitions preserve the baseline and exclude untouched fields"
        );

        // Discard the edits and restore the starting values.
        session.RevertChanges();
        Console.WriteLine(order.CustomerName); // Alice
        Console.WriteLine(session.HasChanges); // False
        DocsCheck.Require(order.CustomerName == "Alice" && !session.HasChanges, "reverting restores the model and clears dirty state");
        // /sample
    }
}

// sample: readme-quickstart
[SparseModel]
public partial class Order
{
    public string CustomerName { get; set; } = "";
    public int Quantity { get; set; }
}
// /sample
