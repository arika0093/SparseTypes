# SparseTypes.OpenApi.Swashbuckle

Register a generated `ChangePayload` with Swashbuckle:

<!-- illustrative: Replace Order with a model generated in your application. -->
```csharp
builder.Services.AddSwaggerGen();
builder.Services.AddSparseOpenApi<Order.ChangePayload>();
```

The registration adds the payload and its generated change variants to `components.schemas`, including types that no endpoint uses. Schema component names come from the model's namespace and generated type role, so they stay stable across builds.

This package targets .NET 10. Add `SparseTypes` to the project so its source generator can create the `ChangePayload` schema metadata.
