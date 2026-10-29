# SparseTypes.OpenApi.AspNetCore

Register the model's generated schema bundle with `builder.Services.AddSparseOpenApi(Order.PayloadJson.Schema)`. The bundle includes the envelope and generated payload schemas for nested models, so you do not need to register each nested model's bundle separately.

The adapter selects each document's OpenAPI 3.0 or 3.1 schema. Check `Order.PayloadJson.Schema.RequiredExternalSchemaTypes` for leaf types that need application-provided schemas, then pass `SparsePayloadSchema` descriptors as the second argument to `AddSparseOpenApi`. This includes regular DTO types used as keyed item snapshots. A descriptor contains separate JSON schema strings for OpenAPI 3.0 and 3.1. The adapter does not infer missing schemas from CLR types.

This package targets .NET 10. Add `SparseTypes` to the project so its source generator can create the `ChangePayload` schema metadata.
