using System.Text.Json;
using SparseTypes;

[SparseModel]
public partial class PayloadRoot
{
    public string? Label { get; set; }
}

public static class NativeAotDocsSample
{
    public static void Run()
    {
        // sample: native-aot-json
        var before = Optional<PayloadRoot.Fragment?>.Present(
            PayloadRoot.Fragment.From(new PayloadRoot { Label = "before" })
        );
        var after = Optional<PayloadRoot.Fragment?>.Present(
            PayloadRoot.Fragment.From(new PayloadRoot { Label = "after" })
        );
        var changes = PayloadRoot.ChangeSet.Between(before, after);
        var json = JsonSerializer.Serialize(changes.ToPayload(), PayloadRoot.PayloadJson.Default);
        var restored = JsonSerializer.Deserialize(json, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
        // /sample
        DocsCheck.Require(
            PayloadRoot.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty,
            "source-generated JSON metadata round-trips the change payload"
        );
    }
}
