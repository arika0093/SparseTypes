using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SparseTypes.Generator.Shared;

internal enum SparsePayloadMemberShape
{
    Value,
    NestedModel,
    Sequence,
    KeyedCollection,
    Dictionary,
}

internal enum SparsePayloadOperationKind
{
    Add,
    Remove,
    Update,
    Replace,
}

internal enum SparsePayloadEndpointState
{
    Missing,
    Null,
    Redacted,
    Value,
}

internal enum SparsePayloadDtoRole
{
    Root,
    MembersDelta,
    ModelChange,
    CollectionChange,
    ItemOperation,
    Endpoint,
}

internal sealed record SparsePayloadMemberContract
{
    public SparseMemberModel Member { get; init; }

    public string ClrName { get; init; } = string.Empty;

    public string JsonName { get; init; } = string.Empty;

    public SparsePayloadMemberShape Shape { get; init; }

    public bool IsTransportIncluded { get; init; }

    public bool IsJsonIncluded { get; init; }

    public bool RedactBefore { get; init; }

    public bool IsNullable { get; init; }

    public string? KeyTypeName { get; init; }

    public ImmutableArray<string> KeyPropertyNames { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> KeyJsonPropertyNames { get; init; } = ImmutableArray<string>.Empty;

    public string? UnassignedKeyJson { get; init; }

    public bool HasTemporaryIdentity { get; init; }

    public string? TemporaryKeyJsonPropertyName { get; init; }

    public bool CarriesOrder { get; init; }

    public ImmutableArray<SparsePayloadOperationKind> ChangeOperations { get; init; } = ImmutableArray<SparsePayloadOperationKind>.Empty;

    public ImmutableArray<SparsePayloadOperationKind> ItemOperations { get; init; } = ImmutableArray<SparsePayloadOperationKind>.Empty;

    public string? ModelDependency { get; init; }

    public bool Equals(SparsePayloadMemberContract? other) =>
        other is not null
        && Member.Equals(other.Member)
        && ClrName == other.ClrName
        && JsonName == other.JsonName
        && Shape == other.Shape
        && IsTransportIncluded == other.IsTransportIncluded
        && IsJsonIncluded == other.IsJsonIncluded
        && RedactBefore == other.RedactBefore
        && IsNullable == other.IsNullable
        && KeyTypeName == other.KeyTypeName
        && SparseSequence.Equal(KeyPropertyNames, other.KeyPropertyNames)
        && SparseSequence.Equal(KeyJsonPropertyNames, other.KeyJsonPropertyNames)
        && UnassignedKeyJson == other.UnassignedKeyJson
        && HasTemporaryIdentity == other.HasTemporaryIdentity
        && TemporaryKeyJsonPropertyName == other.TemporaryKeyJsonPropertyName
        && CarriesOrder == other.CarriesOrder
        && SparseSequence.Equal(ChangeOperations, other.ChangeOperations)
        && SparseSequence.Equal(ItemOperations, other.ItemOperations)
        && ModelDependency == other.ModelDependency;

    public override int GetHashCode()
    {
        var hash = Member.GetHashCode();
        hash = unchecked(hash * 31 + ClrName.GetHashCode());
        hash = unchecked(hash * 31 + JsonName.GetHashCode());
        hash = unchecked(hash * 31 + (int)Shape);
        hash = unchecked(hash * 31 + (IsTransportIncluded ? 1 : 0));
        hash = unchecked(hash * 31 + (IsJsonIncluded ? 1 : 0));
        hash = unchecked(hash * 31 + (RedactBefore ? 1 : 0));
        hash = unchecked(hash * 31 + (IsNullable ? 1 : 0));
        hash = unchecked(hash * 31 + (KeyTypeName?.GetHashCode() ?? 0));
        hash = unchecked(hash * 31 + SparseSequence.Hash(KeyPropertyNames));
        hash = unchecked(hash * 31 + SparseSequence.Hash(KeyJsonPropertyNames));
        hash = unchecked(hash * 31 + (UnassignedKeyJson?.GetHashCode() ?? 0));
        hash = unchecked(hash * 31 + (HasTemporaryIdentity ? 1 : 0));
        hash = unchecked(hash * 31 + (TemporaryKeyJsonPropertyName?.GetHashCode() ?? 0));
        hash = unchecked(hash * 31 + (CarriesOrder ? 1 : 0));
        hash = unchecked(hash * 31 + SparseSequence.Hash(ChangeOperations));
        hash = unchecked(hash * 31 + SparseSequence.Hash(ItemOperations));
        return unchecked(hash * 31 + (ModelDependency?.GetHashCode() ?? 0));
    }
}

internal sealed record SparsePayloadContract
{
    public string EnvelopeVersionPropertyName { get; init; } = "version";

    public string EnvelopeSchemaIdentityPropertyName { get; init; } = "schema";

    public string EnvelopeRecordedAtPropertyName { get; init; } = "recordedAt";

    public string EnvelopeChangePropertyName { get; init; } = "change";

    public string OperationKindPropertyName { get; init; } = "kind";

    public string MembersPropertyName { get; init; } = "members";

    public string EndpointStatePropertyName { get; init; } = "state";

    public string EndpointValuePropertyName { get; init; } = "value";

    public string SchemaIdentity { get; init; } = string.Empty;

    public ImmutableArray<SparsePayloadMemberContract> Members { get; init; } = ImmutableArray<SparsePayloadMemberContract>.Empty;

    public ImmutableArray<SparsePayloadMemberContract> TransportMembers { get; init; } = ImmutableArray<SparsePayloadMemberContract>.Empty;

    public ImmutableArray<SparsePayloadMemberContract> JsonMembers { get; init; } = ImmutableArray<SparsePayloadMemberContract>.Empty;

    public ImmutableArray<string> ModelDependencies { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<SparsePayloadOperationKind> ModelChangeOperations { get; init; } = ImmutableArray<SparsePayloadOperationKind>.Empty;

    public ImmutableArray<SparsePayloadEndpointState> EndpointStates { get; init; } = ImmutableArray<SparsePayloadEndpointState>.Empty;

    public ImmutableArray<SparsePayloadDtoRole> DtoRoles { get; init; } = ImmutableArray<SparsePayloadDtoRole>.Empty;

    public bool IgnoreUnknownMembers { get; init; }

    public bool RejectDuplicateProperties { get; init; }

    public bool RejectUnknownOperations { get; init; }

    public bool Equals(SparsePayloadContract? other) =>
        other is not null
        && EnvelopeVersionPropertyName == other.EnvelopeVersionPropertyName
        && EnvelopeSchemaIdentityPropertyName == other.EnvelopeSchemaIdentityPropertyName
        && EnvelopeRecordedAtPropertyName == other.EnvelopeRecordedAtPropertyName
        && EnvelopeChangePropertyName == other.EnvelopeChangePropertyName
        && OperationKindPropertyName == other.OperationKindPropertyName
        && MembersPropertyName == other.MembersPropertyName
        && EndpointStatePropertyName == other.EndpointStatePropertyName
        && EndpointValuePropertyName == other.EndpointValuePropertyName
        && SchemaIdentity == other.SchemaIdentity
        && SparseSequence.Equal(Members, other.Members)
        && SparseSequence.Equal(TransportMembers, other.TransportMembers)
        && SparseSequence.Equal(JsonMembers, other.JsonMembers)
        && SparseSequence.Equal(ModelDependencies, other.ModelDependencies)
        && SparseSequence.Equal(ModelChangeOperations, other.ModelChangeOperations)
        && SparseSequence.Equal(EndpointStates, other.EndpointStates)
        && SparseSequence.Equal(DtoRoles, other.DtoRoles)
        && IgnoreUnknownMembers == other.IgnoreUnknownMembers
        && RejectDuplicateProperties == other.RejectDuplicateProperties
        && RejectUnknownOperations == other.RejectUnknownOperations;

    public override int GetHashCode()
    {
        var hash = EnvelopeVersionPropertyName.GetHashCode();
        hash = unchecked(hash * 31 + EnvelopeSchemaIdentityPropertyName.GetHashCode());
        hash = unchecked(hash * 31 + EnvelopeRecordedAtPropertyName.GetHashCode());
        hash = unchecked(hash * 31 + EnvelopeChangePropertyName.GetHashCode());
        hash = unchecked(hash * 31 + OperationKindPropertyName.GetHashCode());
        hash = unchecked(hash * 31 + MembersPropertyName.GetHashCode());
        hash = unchecked(hash * 31 + EndpointStatePropertyName.GetHashCode());
        hash = unchecked(hash * 31 + EndpointValuePropertyName.GetHashCode());
        hash = unchecked(hash * 31 + SchemaIdentity.GetHashCode());
        hash = unchecked(hash * 31 + SparseSequence.Hash(Members));
        hash = unchecked(hash * 31 + SparseSequence.Hash(TransportMembers));
        hash = unchecked(hash * 31 + SparseSequence.Hash(JsonMembers));
        hash = unchecked(hash * 31 + SparseSequence.Hash(ModelDependencies));
        hash = unchecked(hash * 31 + SparseSequence.Hash(ModelChangeOperations));
        hash = unchecked(hash * 31 + SparseSequence.Hash(EndpointStates));
        hash = unchecked(hash * 31 + SparseSequence.Hash(DtoRoles));
        hash = unchecked(hash * 31 + (IgnoreUnknownMembers ? 1 : 0));
        hash = unchecked(hash * 31 + (RejectDuplicateProperties ? 1 : 0));
        return unchecked(hash * 31 + (RejectUnknownOperations ? 1 : 0));
    }

    public static SparsePayloadContract Create(ImmutableArray<SparseMemberModel> members)
    {
        var contracts = members
            .Select(static member => new SparsePayloadMemberContract
            {
                Member = member,
                ClrName = member.Property.Name,
                JsonName = member.Property.HasExplicitJsonPropertyName
                    ? member.Property.JsonPropertyName ?? member.Property.Name
                    : ToCamelCase(member.Property.Name),
                Shape = GetShape(member),
                IsTransportIncluded = !member.Property.IsTransportIgnored,
                IsJsonIncluded = !member.Property.IsTransportIgnored && !member.Property.IsJsonIgnored,
                RedactBefore = member.RedactBefore,
                IsNullable = member.Property.IsNullable,
                KeyTypeName = member.Collection.KeyTypeName,
                KeyPropertyNames = member.Collection.KeyPropertyNames.IsDefault ? ImmutableArray<string>.Empty : member.Collection.KeyPropertyNames,
                KeyJsonPropertyNames = member.Collection.KeyJsonPropertyNames.IsDefault
                    ? ImmutableArray<string>.Empty
                    : member.Collection.KeyJsonPropertyNames,
                UnassignedKeyJson = member.Collection.UnassignedKeyJson,
                HasTemporaryIdentity = member.Collection.TemporaryKeyPropertyName is not null,
                TemporaryKeyJsonPropertyName = member.Collection.TemporaryKeyJsonPropertyName,
                CarriesOrder = member.Collection.Semantic == SparseCollectionSemantic.KeyedSequence,
                ChangeOperations = GetChangeOperations(GetShape(member)),
                ItemOperations = GetItemOperations(member),
                ModelDependency = GetModelDependency(member),
            })
            .ToImmutableArray();
        var transportMembers = contracts.Where(static member => member.IsTransportIncluded).ToImmutableArray();
        var jsonMembers = contracts.Where(static member => member.IsJsonIncluded).ToImmutableArray();
        var dependencies = contracts
            .Select(static member => member.ModelDependency)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToImmutableArray();

        return new SparsePayloadContract
        {
            SchemaIdentity = CreateSchemaIdentity(jsonMembers),
            Members = contracts,
            TransportMembers = transportMembers,
            JsonMembers = jsonMembers,
            ModelDependencies = dependencies,
            ModelChangeOperations = ImmutableArray.Create(SparsePayloadOperationKind.Update, SparsePayloadOperationKind.Replace),
            EndpointStates = ImmutableArray.Create(
                SparsePayloadEndpointState.Missing,
                SparsePayloadEndpointState.Null,
                SparsePayloadEndpointState.Redacted,
                SparsePayloadEndpointState.Value
            ),
            DtoRoles = ImmutableArray.Create(
                SparsePayloadDtoRole.Root,
                SparsePayloadDtoRole.MembersDelta,
                SparsePayloadDtoRole.ModelChange,
                SparsePayloadDtoRole.CollectionChange,
                SparsePayloadDtoRole.ItemOperation,
                SparsePayloadDtoRole.Endpoint
            ),
            IgnoreUnknownMembers = false,
            RejectDuplicateProperties = true,
            RejectUnknownOperations = true,
        };
    }

    private static string CreateSchemaIdentity(ImmutableArray<SparsePayloadMemberContract> members)
    {
        var canonical = new StringBuilder("SparseTypes.ChangePayload.Schema.v1\n");
        foreach (var member in members.OrderBy(static member => member.JsonName, StringComparer.Ordinal))
        {
            AppendCanonical(canonical, member.JsonName);
            AppendCanonical(canonical, member.Shape.ToString());
            AppendCanonical(canonical, GetWireType(member));
            AppendCanonical(canonical, member.IsNullable ? "nullable" : "required");
            AppendCanonical(canonical, member.RedactBefore ? "redacted-before" : "complete-before");
            AppendCanonical(canonical, member.Member.Collection.Semantic.ToString());
            AppendCanonical(
                canonical,
                member.Member.Collection.Semantic == SparseCollectionSemantic.None ? string.Empty : GetWireType(member.Member.Collection.ElementType)
            );
            if (member.Member.Collection.Semantic != SparseCollectionSemantic.None)
            {
                AppendCanonical(canonical, GetElementNullability(member));
            }
            AppendCanonical(canonical, member.Member.Collection.ValueType is SparseTypeModel valueType ? GetWireType(valueType) : string.Empty);
            if (member.Member.Collection.ValueType is not null)
            {
                AppendCanonical(canonical, GetValueNullability(member));
            }
            AppendCanonical(canonical, member.KeyTypeName is string keyTypeName ? GetWireTypeName(keyTypeName) : string.Empty);
            AppendCanonical(canonical, member.HasTemporaryIdentity ? "temporary-key" : "stable-key");
            AppendCanonical(canonical, member.CarriesOrder ? "ordered" : "unordered");
            foreach (var keyName in member.KeyPropertyNames.OrderBy(static name => name, StringComparer.Ordinal))
            {
                AppendCanonical(canonical, keyName);
            }
        }

        using var sha256 = SHA256.Create();
        var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        var hexadecimal = new StringBuilder(digest.Length * 2);
        foreach (var item in digest)
        {
            hexadecimal.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }
        return "sha256:" + hexadecimal;
    }

    private static string GetWireType(SparseTypeModel type)
    {
        if (type.PayloadSchemaShape is string payloadSchemaShape)
        {
            return "nested-model:" + payloadSchemaShape;
        }

        if (type.EnumUnderlyingTypeName is string enumUnderlyingTypeName)
        {
            return GetWireTypeName(enumUnderlyingTypeName);
        }

        return GetWireTypeName(type.NonNullableName);
    }

    private static string GetWireType(SparsePayloadMemberContract member)
    {
        var collection = member.Member.Collection;
        if (collection.Semantic == SparseCollectionSemantic.None)
        {
            return GetWireType(member.Member.Property.Type);
        }

        return "collection:"
            + collection.Semantic
            + ":"
            + GetWireType(collection.ElementType)
            + ":"
            + (collection.ValueType is SparseTypeModel valueType ? GetWireType(valueType) : string.Empty);
    }

    private static string GetElementNullability(SparsePayloadMemberContract member)
    {
        if (member.Member.Collection.Semantic == SparseCollectionSemantic.None)
        {
            return string.Empty;
        }

        return member.Member.Collection.ElementType.IsNullable ? "nullable-element" : "required-element";
    }

    private static string GetValueNullability(SparsePayloadMemberContract member)
    {
        if (member.Member.Collection.ValueType is not SparseTypeModel valueType)
        {
            return string.Empty;
        }

        return valueType.IsNullable ? "nullable-value" : "required-value";
    }

    internal static string GetWireTypeName(string typeName)
    {
        var normalized = typeName.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized.Substring("global::".Length);
        }
        return normalized switch
        {
            "long" or "System.Int64" => "int64-string",
            "ulong" or "System.UInt64" => "uint64-string",
            "decimal" or "System.Decimal" => "decimal-string",
            "System.Guid" => "guid-string",
            "System.DateTime" => "datetime-string",
            "System.DateTimeOffset" => "datetimeoffset-string",
            "string" or "System.String" => "string",
            "bool" or "System.Boolean" => "boolean",
            "byte"
            or "System.Byte"
            or "sbyte"
            or "System.SByte"
            or "short"
            or "System.Int16"
            or "ushort"
            or "System.UInt16"
            or "int"
            or "System.Int32"
            or "uint"
            or "System.UInt32" => "integer-number",
            "float" or "System.Single" or "double" or "System.Double" => "floating-number",
            _ => "external:" + normalized,
        };
    }

    private static void AppendCanonical(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var characters = name.ToCharArray();
        var charactersToConvert = 0;
        while (charactersToConvert < characters.Length)
        {
            if (charactersToConvert == 1 && !char.IsUpper(characters[charactersToConvert]))
            {
                break;
            }

            if (charactersToConvert > 0 && charactersToConvert + 1 < characters.Length && !char.IsUpper(characters[charactersToConvert + 1]))
            {
                break;
            }

            characters[charactersToConvert] = char.ToLowerInvariant(characters[charactersToConvert]);
            charactersToConvert++;
        }

        return new string(characters);
    }

    private static ImmutableArray<SparsePayloadOperationKind> GetChangeOperations(SparsePayloadMemberShape shape) =>
        shape switch
        {
            SparsePayloadMemberShape.NestedModel or SparsePayloadMemberShape.KeyedCollection or SparsePayloadMemberShape.Dictionary =>
                ImmutableArray.Create(SparsePayloadOperationKind.Update, SparsePayloadOperationKind.Replace),
            _ => ImmutableArray.Create(SparsePayloadOperationKind.Replace),
        };

    private static ImmutableArray<SparsePayloadOperationKind> GetItemOperations(SparseMemberModel member)
    {
        if (member.Collection.Semantic is not (SparseCollectionSemantic.KeyedSequence or SparseCollectionSemantic.Dictionary))
        {
            return ImmutableArray<SparsePayloadOperationKind>.Empty;
        }

        var isModelItem =
            member.Collection.Semantic == SparseCollectionSemantic.KeyedSequence
                ? member.Collection.ElementType.IsFragmentModel
                : member.Collection.ValueType?.IsFragmentModel == true;
        return isModelItem
            ? ImmutableArray.Create(
                SparsePayloadOperationKind.Add,
                SparsePayloadOperationKind.Remove,
                SparsePayloadOperationKind.Update,
                SparsePayloadOperationKind.Replace
            )
            : ImmutableArray.Create(SparsePayloadOperationKind.Add, SparsePayloadOperationKind.Remove, SparsePayloadOperationKind.Replace);
    }

    private static string? GetModelDependency(SparseMemberModel member)
    {
        if (member.ChildModel is not null)
        {
            return member.ChildModel.Value.NonNullableName;
        }

        if (member.Collection.ElementType.IsFragmentModel)
        {
            return member.Collection.ElementType.NonNullableName;
        }

        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            return member.Collection.ValueType.Value.NonNullableName;
        }

        return null;
    }

    private static SparsePayloadMemberShape GetShape(SparseMemberModel member)
    {
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            return SparsePayloadMemberShape.NestedModel;
        }

        if (SparseChangeSetBasicsEmitter.IsKeyed(member))
        {
            return SparsePayloadMemberShape.KeyedCollection;
        }

        if (SparseChangeSetBasicsEmitter.IsDict(member))
        {
            return SparsePayloadMemberShape.Dictionary;
        }

        return member.Collection.Semantic == SparseCollectionSemantic.None ? SparsePayloadMemberShape.Value : SparsePayloadMemberShape.Sequence;
    }
}
