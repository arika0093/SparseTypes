using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits OpenAPI 3.0 and 3.1 schema bundles from the payload contract.</summary>
internal static class SparsePayloadSchemaEmitter
{
    internal static void AppendSchemaProperty(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        if (context.Dialect.PayloadSchemaNamesType is null)
        {
            throw new InvalidOperationException("PayloadSchemaNamesType must be configured when emitting payload schemas.");
        }

        var schema = new SchemaBuilder(context);
        var descriptors = BuildDescriptors(context, schema);
        var descriptorType = context.Dialect.PayloadSchemaDescriptorType!;
        var bundleType = context.Dialect.PayloadSchemaBundleType!;
        var optionsType = context.Dialect.PayloadSchemaBundleOptionsType;
        var identityType = context.Dialect.PayloadSchemaIdentityType;
        var schema30Member = context.Dialect.PayloadSchemaOptionsSchema30MemberName;
        var schema31Member = context.Dialect.PayloadSchemaOptionsSchema31MemberName;
        var identitiesMember = context.Dialect.PayloadSchemaOptionsIdentitiesMemberName;
        var externalTypesMember = context.Dialect.PayloadSchemaOptionsExternalTypesMemberName;
        var dependenciesMember = context.Dialect.PayloadSchemaOptionsDependenciesMemberName;
        if (
            optionsType is null
            || identityType is null
            || schema30Member is null
            || schema31Member is null
            || identitiesMember is null
            || externalTypesMember is null
            || dependenciesMember is null
        )
        {
            throw new InvalidOperationException("Payload schema option types and member names must be configured when emitting payload schemas.");
        }

        code.AppendLine("/// <summary>Gets the generated OpenAPI 3.0 and 3.1 payload schema bundle.</summary>");
        code.AppendLine("public static " + bundleType + " Schema { get; } = new " + bundleType + "(");
        code.Indent();
        code.AppendLine("typeof(ChangePayload),");
        code.AppendLine("new " + optionsType);
        code.OpenBlock();
        code.Append(schema30Member).Append(" = static () => ");
        AppendDescriptorArray(code, descriptorType, descriptors, schema, openApi31: false);
        code.AppendLine(",");
        code.Append(schema31Member).Append(" = static () => ");
        AppendDescriptorArray(code, descriptorType, descriptors, schema, openApi31: true);
        code.AppendLine(",");
        code.Append(identitiesMember).Append(" = new ").Append(identityType).Append("[] { ");
        code.Append(
            string.Join(
                ", ",
                descriptors.Select(descriptor => "new(typeof(" + descriptor.Type + "), " + SymbolDisplay.FormatLiteral(descriptor.Name, true) + ")")
            )
        );
        code.AppendLine(" },");
        code.Append(externalTypesMember).Append(" = new global::System.Type[] { ");
        code.Append(string.Join(", ", schema.ExternalTypes.Select(static type => "typeof(" + type + ")")));
        code.AppendLine(" },");
        code.Append(dependenciesMember).Append(" = new global::System.Func<" + bundleType + ">[] { ");
        code.Append(
            string.Join(
                ", ",
                context
                    .Contract.ModelDependencies.Where(dependency => !string.Equals(dependency, context.ModelType, StringComparison.Ordinal))
                    .Select(dependency => "() => " + dependency + ".PayloadJson.Schema")
            )
        );
        code.AppendLine(" }");
        code.CloseBlock();
        code.Unindent();
        code.AppendLine(");");
    }

    private static void AppendDescriptorArray(
        SharedIndentedBuilder code,
        string descriptorType,
        IReadOnlyList<SchemaDescriptor> descriptors,
        SchemaBuilder schema,
        bool openApi31
    )
    {
        code.AppendLine("new " + descriptorType + "[]");
        code.OpenBlock();
        foreach (var descriptor in descriptors)
        {
            var json = openApi31 ? descriptor.OpenApi31 : descriptor.OpenApi30;
            code.Append("new(");
            code.Append("typeof(").Append(descriptor.Type).Append("), ");
            code.Append(SymbolDisplay.FormatLiteral(descriptor.Name, true)).Append(", ");
            code.Append(schema.SourceExpression(json, openApi31));
            code.AppendLine("),");
        }
        code.Unindent().Append("}");
    }

    private static IReadOnlyList<SchemaDescriptor> BuildDescriptors(PayloadJsonEmissionContext context, SchemaBuilder schema)
    {
        var descriptors = new List<SchemaDescriptor>();
        var modelChange = SparseChangeSetPayloadEmitter.PayloadTypeName(context.Dialect, context.ModelType, "ModelChange");
        var snapshot = SparseChangeSetPayloadEmitter.PayloadTypeName(context.Dialect, context.ModelType, "Root");
        var membersDelta = SparseChangeSetPayloadEmitter.PayloadTypeName(context.Dialect, context.ModelType, "MembersDelta");
        descriptors.Add(
            new SchemaDescriptor(
                "ChangePayload",
                SchemaComponentName(context.ModelType, "ChangePayload"),
                BuildRootSchema(context, schema, modelChange, openApi31: false),
                BuildRootSchema(context, schema, modelChange, openApi31: true)
            )
        );
        descriptors.Add(
            new SchemaDescriptor(
                modelChange,
                SchemaComponentName(context.ModelType, "ModelChange"),
                BuildModelChangeSchema(context, schema, snapshot, membersDelta, openApi31: false),
                BuildModelChangeSchema(context, schema, snapshot, membersDelta, openApi31: true)
            )
        );
        descriptors.Add(
            new SchemaDescriptor(
                snapshot,
                SchemaComponentName(context.ModelType, "Root"),
                BuildSnapshotSchema(context, schema, openApi31: false),
                BuildSnapshotSchema(context, schema, openApi31: true)
            )
        );
        descriptors.Add(
            new SchemaDescriptor(
                membersDelta,
                SchemaComponentName(context.ModelType, "MembersDelta"),
                BuildMembersDeltaSchema(context, schema),
                BuildMembersDeltaSchema(context, schema)
            )
        );

        foreach (var memberContract in context.Contract.JsonMembers)
        {
            var member = memberContract.Member;
            var memberType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(context.Dialect, context.ModelType, "Change", member.Id);
            descriptors.Add(
                new SchemaDescriptor(
                    memberType,
                    SchemaMemberName(context.ModelType, memberContract.JsonName),
                    BuildMemberChangeSchema(context, schema, member, memberContract.JsonName, openApi31: false),
                    BuildMemberChangeSchema(context, schema, member, memberContract.JsonName, openApi31: true)
                )
            );

            if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
            {
                var itemType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(context.Dialect, context.ModelType, "Item", member.Id);
                descriptors.Add(
                    new SchemaDescriptor(
                        itemType,
                        SchemaItemName(context.ModelType, memberContract.JsonName),
                        BuildItemSchema(context, schema, member, openApi31: false),
                        BuildItemSchema(context, schema, member, openApi31: true)
                    )
                );
            }
        }

        return descriptors;
    }

    private static string BuildRootSchema(PayloadJsonEmissionContext context, SchemaBuilder schema, string modelChange, bool openApi31) =>
        Object(
            ("type", String("object")),
            (
                "properties",
                Object(
                    (context.Contract.EnvelopeVersionPropertyName, SingleValue("string", context.Dialect.ChangePayloadVersion, openApi31)),
                    (
                        context.Contract.EnvelopeChangePropertyName,
                        schema.Reference(modelChange, SchemaComponentName(context.ModelType, "ModelChange"))
                    )
                )
            ),
            ("required", Array(String(context.Contract.EnvelopeVersionPropertyName), String(context.Contract.EnvelopeChangePropertyName)))
        );

    private static string BuildModelChangeSchema(
        PayloadJsonEmissionContext context,
        SchemaBuilder schema,
        string snapshot,
        string membersDelta,
        bool openApi31
    )
    {
        var kind = context.Contract.OperationKindPropertyName;
        var update = Object(
            ("type", String("object")),
            (
                "properties",
                Object(
                    (kind, SingleValue("string", "update", openApi31)),
                    (context.Contract.MembersPropertyName, schema.Reference(membersDelta, SchemaComponentName(context.ModelType, "MembersDelta")))
                )
            ),
            ("required", Array(String(kind), String(context.Contract.MembersPropertyName))),
            ("not", Object(("required", Array(String("before"), String("after")))))
        );
        var replace = Object(
            ("type", String("object")),
            (
                "properties",
                Object(
                    (kind, SingleValue("string", "replace", openApi31)),
                    (
                        "before",
                        Endpoint(
                            schema.Reference(snapshot, SchemaComponentName(context.ModelType, "Root")),
                            includeRedacted: true,
                            allowNull: true,
                            openApi31
                        )
                    ),
                    (
                        "after",
                        Endpoint(
                            schema.Reference(snapshot, SchemaComponentName(context.ModelType, "Root")),
                            includeRedacted: false,
                            allowNull: true,
                            openApi31
                        )
                    )
                )
            ),
            ("required", Array(String(kind), String("before"), String("after"))),
            ("not", Object(("required", Array(String(context.Contract.MembersPropertyName)))))
        );
        return Object(("oneOf", Array(update, replace)), ("discriminator", Object(("propertyName", String(kind)))));
    }

    private static string BuildSnapshotSchema(PayloadJsonEmissionContext context, SchemaBuilder schema, bool openApi31)
    {
        var properties = new List<(string, string)>();
        foreach (var contractMember in context.Contract.JsonMembers.Select(static item => (item.JsonName, item.Member)))
        {
            var member = contractMember.Member;
            properties.Add(
                (
                    contractMember.JsonName,
                    Endpoint(MemberValueSchema(context, schema, member), includeRedacted: false, allowNull: member.Property.IsNullable, openApi31)
                )
            );
        }

        return Object(("type", String("object")), ("properties", Object(properties.ToArray())));
    }

    private static string BuildMembersDeltaSchema(PayloadJsonEmissionContext context, SchemaBuilder schema)
    {
        var properties = context
            .Contract.JsonMembers.Select(contractMember =>
            {
                var type = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
                    context.Dialect,
                    context.ModelType,
                    "Change",
                    contractMember.Member.Id
                );
                return (contractMember.JsonName, schema.Reference(type, SchemaMemberName(context.ModelType, contractMember.JsonName)));
            })
            .ToArray();
        return Object(("type", String("object")), ("properties", Object(properties)));
    }

    private static string BuildMemberChangeSchema(
        PayloadJsonEmissionContext context,
        SchemaBuilder schema,
        SparseMemberModel member,
        string jsonName,
        bool openApi31
    )
    {
        var kind = context.Contract.OperationKindPropertyName;
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var child = member.ChildModel!.Value.NonNullableName;
            var childChange = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                context.Dialect,
                child,
                "ModelChange",
                context.ImplementationNamespace
            );
            return schema.Reference(childChange, SchemaComponentName(child, "ModelChange"));
        }

        if (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
        {
            var itemType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(context.Dialect, context.ModelType, "Item", member.Id);
            var valueSchema = MemberValueSchema(context, schema, member);
            var replace = Object(
                ("type", String("object")),
                (
                    "properties",
                    Object(
                        (kind, SingleValue("string", "replace", openApi31)),
                        ("before", Endpoint(valueSchema, includeRedacted: true, allowNull: member.Property.IsNullable, openApi31)),
                        ("after", Endpoint(valueSchema, includeRedacted: false, allowNull: member.Property.IsNullable, openApi31))
                    )
                ),
                ("required", Array(String(kind), String("before"), String("after"))),
                ("not", Object(("required", Array(String("items")))))
            );
            var updateProperties = new List<(string, string)>
            {
                (kind, SingleValue("string", "update", openApi31)),
                ("items", Object(("type", String("array")), ("items", schema.Reference(itemType, SchemaItemName(context.ModelType, jsonName))))),
            };
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                var keySchema = schema.TypeSchema(keyType);
                updateProperties.Add(("beforeOrder", Object(("type", String("array")), ("items", keySchema))));
                updateProperties.Add(("afterOrder", Object(("type", String("array")), ("items", keySchema))));
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    var guidSchema = Object(("type", String("string")), ("format", String("uuid")));
                    updateProperties.Add(("tempBeforeOrder", Object(("type", String("array")), ("items", guidSchema))));
                    updateProperties.Add(("tempAfterOrder", Object(("type", String("array")), ("items", guidSchema))));
                }
            }

            var update = Object(
                ("type", String("object")),
                ("properties", Object(updateProperties.ToArray())),
                ("required", Array(String(kind), String("items"))),
                ("not", Object(("required", Array(String("before"), String("after")))))
            );
            return Object(("oneOf", Array(replace, update)), ("discriminator", Object(("propertyName", String(kind)))));
        }

        return Object(
            ("type", String("object")),
            (
                "properties",
                Object(
                    (kind, SingleValue("string", "replace", openApi31)),
                    (
                        "before",
                        Endpoint(MemberValueSchema(context, schema, member), includeRedacted: true, allowNull: member.Property.IsNullable, openApi31)
                    ),
                    (
                        "after",
                        Endpoint(MemberValueSchema(context, schema, member), includeRedacted: false, allowNull: member.Property.IsNullable, openApi31)
                    )
                )
            ),
            ("required", Array(String(kind), String("after")))
        );
    }

    private static string BuildItemSchema(PayloadJsonEmissionContext context, SchemaBuilder schema, SparseMemberModel member, bool openApi31)
    {
        var key = schema.TypeSchema(SparseChangeSetBasicsEmitter.KeyTypeOf(member));
        var valueType = SparseChangeSetBasicsEmitter.IsKeyed(member) ? member.Collection.ElementType : member.Collection.ValueType!.Value;
        var isModelValue = valueType.IsFragmentModel;
        var valueSchema = isModelValue
            ? schema.Reference(
                SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    context.Dialect,
                    valueType.NonNullableName,
                    "Root",
                    context.ImplementationNamespace
                ),
                SchemaComponentName(valueType.NonNullableName, "Root")
            )
            : schema.TypeSchema(valueType);
        var properties = new List<(string, string)>
        {
            ("key", key),
            ("kind", Object(("type", String("string")), ("enum", Array(String("add"), String("remove"), String("replace"), String("update"))))),
            ("before", Endpoint(valueSchema, includeRedacted: true, allowNull: valueType.IsReferenceType, openApi31)),
            ("after", Endpoint(valueSchema, includeRedacted: false, allowNull: valueType.IsReferenceType, openApi31)),
            ("beforeIndex", Object(("type", String("integer")), ("minimum", "0"))),
            ("afterIndex", Object(("type", String("integer")), ("minimum", "0"))),
            ("isReordered", BooleanTrue(openApi31)),
        };
        if (isModelValue)
        {
            var childChange = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                context.Dialect,
                valueType.NonNullableName,
                "MembersDelta",
                context.ImplementationNamespace
            );
            properties.Add(("members", schema.Reference(childChange, SchemaComponentName(valueType.NonNullableName, "MembersDelta"))));
        }

        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member) && SparseChangeSetBasicsEmitter.IsKeyed(member))
        {
            properties.Add(("temporaryKey", Object(("type", String("string")), ("format", String("uuid")))));
        }

        var add = ItemVariant(properties, "add", required: ["key", "kind", "after"], notRequired: ["before"], openApi31);
        var remove = ItemVariant(properties, "remove", required: ["key", "kind", "before"], notRequired: ["after"], openApi31);
        var replace = ItemVariant(properties, "replace", required: ["key", "kind", "before", "after"], notRequired: [], openApi31);
        var updateRequirements = isModelValue
            ? Object(("anyOf", Array(Object(("required", Array(String("members")))), Object(("required", Array(String("isReordered")))))))
            : Object(("required", Array(String("isReordered"))));
        var update = ItemVariant(properties, "update", required: ["key", "kind"], notRequired: [], openApi31, extra: updateRequirements);
        return Object(("oneOf", Array(add, remove, replace, update)), ("discriminator", Object(("propertyName", String("kind")))));
    }

    private static string ItemVariant(
        IReadOnlyList<(string Name, string Schema)> properties,
        string kind,
        string[] required,
        string[] notRequired,
        bool openApi31,
        string? extra = null
    )
    {
        var variantProperties = properties
            .Select(property => property.Name == "kind" ? (property.Name, SingleValue("string", kind, openApi31)) : property)
            .ToArray();
        var entries = new List<(string, string)>
        {
            ("type", String("object")),
            ("properties", Object(variantProperties)),
            ("required", Array(required.Select(String).ToArray())),
        };
        if (notRequired.Length > 0)
        {
            entries.Add(("not", Object(("required", Array(notRequired.Select(String).ToArray())))));
        }

        if (extra is not null)
        {
            entries.Add(("allOf", Array(extra)));
        }

        return Object(entries.ToArray());
    }

    private static string MemberValueSchema(PayloadJsonEmissionContext context, SchemaBuilder schema, SparseMemberModel member)
    {
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var child = member.ChildModel!.Value.NonNullableName;
            return schema.Reference(
                SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(context.Dialect, child, "Root", context.ImplementationNamespace),
                SchemaComponentName(child, "Root")
            );
        }

        if (SparseChangeSetBasicsEmitter.IsKeyed(member))
        {
            return Object(("type", String("array")), ("items", ItemValueSchema(context, schema, member.Collection.ElementType)));
        }

        if (SparseChangeSetBasicsEmitter.IsDict(member))
        {
            return Object(("type", String("object")), ("additionalProperties", ItemValueSchema(context, schema, member.Collection.ValueType!.Value)));
        }

        if (member.Collection.Semantic == SparseCollectionSemantic.ScalarSequence && !IsByteArray(member.Property.Type.RuntimeName))
        {
            return Object(("type", String("array")), ("items", ItemValueSchema(context, schema, member.Collection.ElementType)));
        }

        return schema.TypeSchema(member.Property.Type);
    }

    private static string ItemValueSchema(PayloadJsonEmissionContext context, SchemaBuilder schema, SparseTypeModel valueType)
    {
        if (valueType.IsFragmentModel)
        {
            return schema.Reference(
                SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    context.Dialect,
                    valueType.NonNullableName,
                    "Root",
                    context.ImplementationNamespace
                ),
                SchemaComponentName(valueType.NonNullableName, "Root")
            );
        }

        return schema.TypeSchema(valueType);
    }

    private static string Endpoint(string valueSchema, bool includeRedacted, bool allowNull, bool openApi31)
    {
        var states = new List<string> { "missing" };
        if (allowNull)
        {
            states.Add("null");
        }
        if (includeRedacted)
        {
            states.Add("redacted");
        }
        states.Add("value");
        var variants = new List<string>();
        foreach (var state in states)
        {
            if (state == "value")
            {
                variants.Add(
                    Object(
                        ("type", String("object")),
                        ("properties", Object(("state", SingleValue("string", state, openApi31)), ("value", valueSchema))),
                        ("required", Array(String("state"), String("value")))
                    )
                );
            }
            else
            {
                variants.Add(
                    Object(
                        ("type", String("object")),
                        ("properties", Object(("state", SingleValue("string", state, openApi31)))),
                        ("required", Array(String("state"))),
                        ("not", Object(("required", Array(String("value")))))
                    )
                );
            }
        }

        return Object(("oneOf", Array(variants.ToArray())), ("discriminator", Object(("propertyName", String("state")))));
    }

    private static string SingleValue(string type, string value, bool openApi31) =>
        openApi31 ? Object(("type", String(type)), ("const", String(value))) : Object(("type", String(type)), ("enum", Array(String(value))));

    private static string BooleanTrue(bool openApi31) =>
        openApi31 ? Object(("type", String("boolean")), ("const", "true")) : Object(("type", String("boolean")), ("enum", Array("true")));

    private static string Object(params (string Name, string Schema)[] properties)
    {
        var builder = new StringBuilder("{");
        for (var index = 0; index < properties.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }

            builder.Append(JsonString(properties[index].Name)).Append(':').Append(properties[index].Schema);
        }

        return builder.Append('}').ToString();
    }

    private static string Array(params string[] values)
    {
        var builder = new StringBuilder("[");
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }

            builder.Append(values[index]);
        }

        return builder.Append(']').ToString();
    }

    private static string String(string value) => JsonString(value);

    private static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static bool IsByteArray(string typeName)
    {
        var normalized = typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName.Substring(8) : typeName;
        return normalized is "System.Byte[]" or "byte[]";
    }

    private static string SchemaComponentName(string modelType, string role) => SchemaBaseName(modelType) + "_" + role;

    private static string SchemaMemberName(string modelType, string jsonName) => SchemaComponentName(modelType, "Change_" + SchemaNamePart(jsonName));

    private static string SchemaItemName(string modelType, string jsonName) => SchemaComponentName(modelType, "Item_" + SchemaNamePart(jsonName));

    private static string SchemaBaseName(string modelType)
    {
        var identity = modelType.StartsWith("global::", StringComparison.Ordinal) ? modelType.Substring(8) : modelType;
        var genericStart = identity.IndexOf('<');
        var genericArguments = string.Empty;
        if (genericStart >= 0)
        {
            genericArguments = "_" + SchemaNamePart(identity[(genericStart + 1)..^1]);
            identity = identity[..genericStart];
        }

        return identity.Replace('.', '_').Replace('+', '_') + genericArguments;
    }

    private static string SchemaNamePart(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (character == '_')
            {
                builder.Append("__");
            }
            else
            {
                builder.Append("_x").Append(((int)character).ToString("X4")).Append('_');
            }
        }

        return builder.ToString();
    }

    private sealed record SchemaDescriptor(string Type, string Name, string OpenApi30, string OpenApi31);

    private sealed class SchemaBuilder(PayloadJsonEmissionContext context)
    {
        private const string ReferenceMarkerPrefix = "__SPARSE_SCHEMA_REF_";
        private readonly Dictionary<string, (string Type, string? Name)> _references = new(StringComparer.Ordinal);
        private readonly HashSet<string> _externalTypes = new(StringComparer.Ordinal);
        private int _referenceIndex;

        internal IEnumerable<string> ExternalTypes => _externalTypes;

        internal string Reference(string type, string? componentName = null)
        {
            var marker = ReferenceMarkerPrefix + _referenceIndex++ + "__";
            _references.Add(marker, (type, componentName));
            return Object(("$ref", String("#/components/schemas/" + marker)));
        }

        internal string TypeSchema(string typeName)
        {
            var normalized = NormalizeTypeName(typeName);
            var schema = normalized switch
            {
                "string" or "System.String" => Object(("type", String("string"))),
                "bool" or "System.Boolean" => Object(("type", String("boolean"))),
                "byte"
                or "sbyte"
                or "short"
                or "ushort"
                or "int"
                or "uint"
                or "System.Byte"
                or "System.SByte"
                or "System.Int16"
                or "System.UInt16"
                or "System.Int32"
                or "System.UInt32" => Object(("type", String("integer")), ("format", String("int32"))),
                "long" or "System.Int64" => Object(
                    ("type", String("string")),
                    ("pattern", String("^-?(0|[1-9][0-9]*)$")),
                    ("x-sparse-type", String("int64"))
                ),
                "ulong" or "System.UInt64" => Object(
                    ("type", String("string")),
                    ("pattern", String("^(0|[1-9][0-9]*)$")),
                    ("x-sparse-type", String("uint64"))
                ),
                "float" or "System.Single" => Object(("type", String("number")), ("format", String("float"))),
                "double" or "System.Double" => Object(("type", String("number")), ("format", String("double"))),
                "decimal" or "System.Decimal" => Object(
                    ("type", String("string")),
                    ("pattern", String("^-?(0|[1-9][0-9]*)(\\.[0-9]+)?$")),
                    ("x-sparse-type", String("decimal"))
                ),
                "char" or "System.Char" => Object(("type", String("string")), ("minLength", "1"), ("maxLength", "1")),
                "System.Guid" => Object(
                    ("type", String("string")),
                    ("format", String("uuid")),
                    ("pattern", String("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))
                ),
                "System.DateTime" => Object(
                    ("type", String("string")),
                    ("pattern", String("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{7}(Z)?$")),
                    ("x-sparse-type", String("date-time-utc-or-unspecified"))
                ),
                "System.DateTimeOffset" => Object(
                    ("type", String("string")),
                    ("pattern", String("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{7}(Z|[+-]\\d{2}:\\d{2})$")),
                    ("x-sparse-type", String("date-time-offset"))
                ),
                "System.DateOnly" => Object(("type", String("string")), ("format", String("date"))),
                "System.TimeOnly" => Object(("type", String("string")), ("format", String("time"))),
                "System.TimeSpan" => Object(("type", String("string")), ("format", String("duration"))),
                "System.Uri" => Object(("type", String("string")), ("format", String("uri"))),
                "System.Byte[]" or "byte[]" => Object(("type", String("string")), ("format", String("byte"))),
                _ => null,
            };
            if (schema is not null)
            {
                return schema;
            }

            var type = TypeExpression(typeName);
            _externalTypes.Add(type);
            return Reference(type);
        }

        internal string TypeSchema(SparseTypeModel type)
        {
            if (
                type.EnumUnderlyingTypeName is string underlyingTypeName
                && (NormalizeTypeName(underlyingTypeName) is "long" or "ulong" or "System.Int64" or "System.UInt64")
            )
            {
                return TypeSchema(underlyingTypeName);
            }

            return TypeSchema(type.NonNullableName);
        }

        internal string SourceExpression(string json, bool openApi31)
        {
            _ = openApi31;
            var builder = new StringBuilder();
            var index = 0;
            while (index < json.Length)
            {
                var markerStart = json.IndexOf(ReferenceMarkerPrefix, index, StringComparison.Ordinal);
                if (markerStart < 0)
                {
                    builder.Append(SymbolDisplay.FormatLiteral(json[index..], quote: true));
                    break;
                }

                var markerEnd = json.IndexOf("__", markerStart + ReferenceMarkerPrefix.Length, StringComparison.Ordinal);
                if (markerEnd < 0)
                {
                    throw new InvalidOperationException("A generated schema reference marker is malformed.");
                }

                markerEnd += 2;
                builder.Append(SymbolDisplay.FormatLiteral(json[index..markerStart], quote: true));
                builder.Append(" + ");
                var reference = _references[json[markerStart..markerEnd]];
                builder.Append(reference.Name is null ? NameExpression(reference.Type) : SymbolDisplay.FormatLiteral(reference.Name, true));
                builder.Append(" + ");
                index = markerEnd;
            }

            return builder.ToString();
        }

        private string NameExpression(string type) => context.Dialect.PayloadSchemaNamesType + ".GetName(typeof(" + type + "))";

        private static string TypeExpression(string typeName)
        {
            var type = typeName.Trim();
            if (type.EndsWith("?", StringComparison.Ordinal))
            {
                type = type[..^1];
            }

            const string nullablePrefix = "global::System.Nullable<";
            if (type.StartsWith(nullablePrefix, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
            {
                type = type[nullablePrefix.Length..^1];
            }

            return type;
        }

        private static string NormalizeTypeName(string typeName)
        {
            var type = typeName.Trim();
            if (type.EndsWith("?", StringComparison.Ordinal))
            {
                type = type[..^1];
            }

            const string nullablePrefix = "global::System.Nullable<";
            if (type.StartsWith(nullablePrefix, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
            {
                type = type[nullablePrefix.Length..^1];
            }

            return type.StartsWith("global::", StringComparison.Ordinal) ? type[8..] : type;
        }
    }
}
