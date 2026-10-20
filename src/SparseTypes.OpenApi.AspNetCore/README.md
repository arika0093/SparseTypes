# SparseTypes.OpenApi.AspNetCore

Register a generated `ChangePayload` with ASP.NET Core OpenAPI:

<!-- illustrative: Replace Order with a model generated in your application. -->
```csharp
builder.Services.AddOpenApi();
builder.Services.AddSparseOpenApi<Order.ChangePayload>();

var app = builder.Build();
app.MapOpenApi();
```

The registration adds the payload and its generated change variants to `components.schemas`, including types that no endpoint uses. Schema component names come from the model's namespace and generated type role, so they stay stable across builds. When a payload references another model (for example a keyed line collection), register that model's payload too so its change schemas use the same stable names:

<!-- illustrative: Replace Order and OrderLine with models generated in your application. -->
```csharp
builder.Services.AddSparseOpenApi<Order.ChangePayload>();
builder.Services.AddSparseOpenApi<OrderLine.ChangePayload>();
```

This package targets .NET 10. Add `SparseTypes` to the project so its source generator can create the `ChangePayload` schema metadata.
