# SparseTypes.OpenApi.Swashbuckle

Register the model's generated schema bundle with `builder.Services.AddSparseOpenApi(Order.PayloadJson.Schema)`. This overload selects OpenAPI 3.0. Pass `OpenApiSpecVersion.OpenApi3_1` as the second argument when Swashbuckle emits OpenAPI 3.1.

The bundle includes the envelope and generated schemas for nested models, so separate registrations are unnecessary. Check `Order.PayloadJson.Schema.RequiredExternalSchemaTypes` for leaf types that need application-provided schemas, then pass `SparsePayloadSchema` descriptors to the overload that accepts an external schema list. Each descriptor contains separate JSON schema strings for OpenAPI 3.0 and 3.1. The adapter does not infer missing schemas from CLR types.

This package targets .NET 10. Add `SparseTypes` to the project so its source generator can create the `ChangePayload` schema metadata.
