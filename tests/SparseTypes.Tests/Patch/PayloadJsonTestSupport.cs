using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace SparseTypes.Tests;

internal static class PayloadJsonTestSupport
{
    internal static string Serialize<TPayload>(TPayload payload)
        where TPayload : notnull => JsonSerializer.Serialize(payload, TypeInfo<TPayload>());

    internal static TPayload Deserialize<TPayload>(string json)
        where TPayload : notnull => JsonSerializer.Deserialize(NormalizeFixture<TPayload>(json), TypeInfo<TPayload>())!;

    internal static string WithSchemaIdentity<TPayload>(string json)
        where TPayload : notnull
    {
        var payload =
            Activator.CreateInstance(typeof(TPayload)) ?? throw new InvalidOperationException("Payload fixture type needs a default instance.");
        var schemaProperty =
            typeof(TPayload).GetProperty("Schema") ?? throw new InvalidOperationException("Payload fixture type has no schema property.");
        var schema =
            schemaProperty.GetValue(payload) as string ?? throw new InvalidOperationException("Payload fixture schema identity is not initialized.");
        return json.StartsWith("{", StringComparison.Ordinal) ? json.Insert(1, "\"schema\":\"" + schema + "\",") : json;
    }

    internal static TPayload RoundTrip<TPayload>(TPayload payload)
        where TPayload : notnull => Deserialize<TPayload>(Serialize(payload));

    private static JsonTypeInfo<TPayload> TypeInfo<TPayload>()
        where TPayload : notnull
    {
        var modelType = typeof(TPayload).DeclaringType ?? throw new InvalidOperationException("Payload type must be nested in its model.");
        var helperType =
            modelType.GetNestedType("PayloadJson", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Model does not expose generated payload JSON metadata.");
        var property =
            helperType.GetProperty("Resolver", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Generated payload JSON metadata has no Resolver property.");
        var resolver = (IJsonTypeInfoResolver)property.GetValue(null)!;
        var options = new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(resolver, new DefaultJsonTypeInfoResolver()) };
        return (JsonTypeInfo<TPayload>)options.GetTypeInfo(typeof(TPayload));
    }

    private static string NormalizeFixture<TPayload>(string json)
        where TPayload : notnull
    {
        using var source = JsonDocument.Parse(json);
        var hasSchema =
            source.RootElement.ValueKind == JsonValueKind.Object
            && source.RootElement.EnumerateObject().Any(static property => property.NameEquals("schema"));
        var fixture = hasSchema ? json : WithSchemaIdentity<TPayload>(json);
        using var document = JsonDocument.Parse(fixture);
        if (
            document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Any(static property => property.NameEquals("change"))
            || !document.RootElement.EnumerateObject().Any(static property => property.NameEquals("changes"))
        )
        {
            return fixture;
        }

        var root = JsonNode.Parse(fixture) as JsonObject;
        if (root is null)
        {
            return fixture;
        }

        if (!root.ContainsKey("change") && root.TryGetPropertyValue("changes", out var changes))
        {
            root.Remove("changes");
            root["change"] = ConvertCore(new JsonObject { ["changes"] = changes?.DeepClone() });
        }

        return root.ToJsonString();
    }

    private static JsonObject ConvertCore(JsonObject core)
    {
        if (core["changes"] is not JsonArray changes)
        {
            return new JsonObject { ["kind"] = "update", ["members"] = new JsonObject() };
        }

        if (
            changes.Count == 1
            && changes[0] is JsonObject rootEntry
            && rootEntry["member"] is JsonValue rootName
            && rootName.GetValue<string>() == "$root"
        )
        {
            var whole = changes[0]!.AsObject();
            return new JsonObject
            {
                ["kind"] = "replace",
                ["before"] = ConvertSnapshotEndpoint(whole["before"]),
                ["after"] = ConvertSnapshotEndpoint(whole["after"]),
            };
        }

        var members = new JsonObject();
        foreach (var entry in changes)
        {
            if (entry is not JsonObject member || member["member"] is not JsonValue memberNameNode)
            {
                continue;
            }

            var memberName = memberNameNode.GetValue<string>();
            var jsonName = char.ToLowerInvariant(memberName[0]) + memberName[1..];
            members[jsonName] = ConvertMember(member);
        }

        return new JsonObject { ["kind"] = "update", ["members"] = members };
    }

    private static JsonObject ConvertMember(JsonObject member)
    {
        if (member["nested"] is JsonObject nested)
        {
            return ConvertCore(nested);
        }

        if (member["items"] is JsonArray items)
        {
            var result = new JsonObject { ["kind"] = "update", ["items"] = ConvertItems(items) };
            Copy(member, result, "beforeOrder", "afterOrder", "tempBeforeOrder", "tempAfterOrder");
            return result;
        }

        var replacement = new JsonObject { ["kind"] = "replace" };
        Copy(member, replacement, "before", "after");
        if (member["value"] is JsonObject value && replacement["after"] is null)
        {
            replacement["after"] = value.DeepClone();
        }

        return replacement;
    }

    private static JsonArray ConvertItems(JsonArray items)
    {
        var result = new JsonArray();
        foreach (var itemNode in items)
        {
            if (itemNode is not JsonObject item)
            {
                result.Add(itemNode?.DeepClone());
                continue;
            }

            var kind = item["kind"]?.GetValue<string>();
            var hasEdit = item["edit"] is JsonObject || item["change"] is JsonObject;
            var newKind = kind switch
            {
                "edit" => hasEdit ? "update" : "replace",
                "reorder" => "update",
                _ => kind,
            };
            var resultItem = new JsonObject { ["kind"] = newKind };
            Copy(item, resultItem, "key", "before", "after", "temporaryKey");
            if (item["beforeIndex"]?.GetValue<int>() is >= 0)
            {
                resultItem["beforeIndex"] = item["beforeIndex"]!.DeepClone();
            }
            if (item["afterIndex"]?.GetValue<int>() is >= 0)
            {
                resultItem["afterIndex"] = item["afterIndex"]!.DeepClone();
            }

            if (item["isReordered"]?.GetValue<bool>() == true || kind == "reorder")
            {
                resultItem["isReordered"] = true;
            }
            if (item["edit"] is JsonObject edit)
            {
                Copy(ConvertCore(edit), resultItem, "members");
            }
            else if (item["change"] is JsonObject change)
            {
                Copy(ConvertCore(change), resultItem, "members");
            }
            result.Add(resultItem);
        }
        return result;
    }

    private static JsonNode? ConvertSnapshotEndpoint(JsonNode? endpointNode)
    {
        if (endpointNode is not JsonObject endpoint || endpoint["value"] is not JsonObject root)
        {
            return endpointNode?.DeepClone();
        }

        var result = (JsonObject)endpoint.DeepClone();
        if (root["members"] is JsonArray members)
        {
            var snapshot = new JsonObject();
            foreach (var memberNode in members)
            {
                if (memberNode is not JsonObject member || member["member"] is not JsonValue nameNode)
                {
                    continue;
                }

                var name = nameNode.GetValue<string>();
                var jsonName = char.ToLowerInvariant(name[0]) + name[1..];
                if (member["value"] is JsonNode value)
                {
                    snapshot[jsonName] = value.DeepClone();
                }
            }

            result["value"] = snapshot;
        }
        return result;
    }

    private static void Copy(JsonObject source, JsonObject destination, params string[] names)
    {
        foreach (var name in names)
        {
            if (source[name] is JsonNode value)
            {
                destination[name] = value.DeepClone();
            }
        }
    }
}
