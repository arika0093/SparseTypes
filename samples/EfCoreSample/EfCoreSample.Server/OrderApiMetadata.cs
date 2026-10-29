using EfCoreSample.Contracts;
using SparseTypes;

namespace EfCoreSample.Server;

internal static class OrderApiMetadata
{
    internal static IReadOnlyList<SparsePayloadSchema> ExternalPayloadSchemas { get; } =
    [
        new SparsePayloadSchema(
            typeof(OrderLineDto),
            SparsePayloadSchemaNames.GetName(typeof(OrderLineDto)),
            """{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"key":{"type":"integer","format":"int32"},"temporaryId":{"type":"string","format":"uuid","nullable":true},"quantity":{"type":"integer","format":"int32"},"unitPrice":{"type":"number","format":"double"},"subtotal":{"type":"number","format":"double"}}}""",
            """{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"key":{"type":"integer","format":"int32"},"temporaryId":{"type":["string","null"],"format":"uuid"},"quantity":{"type":"integer","format":"int32"},"unitPrice":{"type":"number","format":"double"},"subtotal":{"type":"number","format":"double"}}}"""
        ),
    ];
}

public sealed class OrderPayloadSchema
{
    public required string Schema { get; init; }
}

public sealed class OrderConflict
{
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public required string Reason { get; init; }
}
