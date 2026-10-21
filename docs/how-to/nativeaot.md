# Use SparseTypes with NativeAOT

SparseTypes-generated code is AOT-friendly, so ordinary model operations need no special handling.
When serializing a `ChangePayload`, pass the generated JSON metadata to `System.Text.Json`.

## Enable NativeAOT

Set `PublishAot` in the application project:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Then publish for the target runtime identifier, for example with `dotnet publish -c Release -r linux-x64`.

## Serialize and deserialize ChangePayload

Pass the generated `{ModelName}.PayloadJson.Default` metadata to both serializer calls, replacing `ModelName` with the model type's name.
The sample uses `PayloadRoot.PayloadJson.Default`:

<!-- sample: native-aot-json -->
```csharp
var before = Optional<PayloadRoot.Fragment?>.Present(
    PayloadRoot.Fragment.From(new PayloadRoot { Label = "before" })
);
var after = Optional<PayloadRoot.Fragment?>.Present(
    PayloadRoot.Fragment.From(new PayloadRoot { Label = "after" })
);
var changes = PayloadRoot.ChangeSet.Between(before, after);
var json = JsonSerializer.Serialize(changes.ToPayload(), PayloadRoot.PayloadJson.Default);
var restored = JsonSerializer.Deserialize(json, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
```
<!-- /sample -->

These generated metadata overloads cover supported scalar, collection, nested model, and ordinary custom object values.
If a payload contains types outside that contract, such as polymorphic `object` or interface members, custom converters,
or extension data, see [ChangePayload NativeAOT reference](../reference/change-payload.md#nativeaot) for the additional metadata setup.
