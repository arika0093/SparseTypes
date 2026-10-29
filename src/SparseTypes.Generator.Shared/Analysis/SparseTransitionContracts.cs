namespace SparseTypes.Generator.Shared;

/// <summary>Product-owned type names for the read-only ChangeSet transition contracts.</summary>
/// <remarks>Shared emission references these contracts only through this configuration.</remarks>
internal sealed record SparseTransitionContracts(
    string ValueChangeInterfaceType,
    string ChangeSetInterfaceType,
    string KeyedChangeInterfaceType,
    string KeyedItemChangeInterfaceType,
    string TemporaryKeyedChangeInterfaceType,
    string DictionaryChangeInterfaceType,
    string DictionaryItemChangeInterfaceType,
    string StructuralDictionaryItemChangeInterfaceType,
    string ChangeRecordType,
    string ChangeKindType
)
{
    /// <summary>Read-only scalar transition contract (open generic, arity 1).</summary>
    public string ValueChangeInterfaceType { get; init; } = ValueChangeInterfaceType;

    /// <summary>Read-only root/nested change-set contract.</summary>
    public string ChangeSetInterfaceType { get; init; } = ChangeSetInterfaceType;

    /// <summary>Optional model-typed root/nested change-set contract.</summary>
    public string? TypedChangeSetInterfaceType { get; init; }

    /// <summary>Optional typed model-rebase bridge used by persistence integrations.</summary>
    public string? PipelineChangeSetInterfaceType { get; init; }

    /// <summary>Optional typed inverse-application contract.</summary>
    public string? ChangeSetReverterInterfaceType { get; init; }

    /// <summary>Read-only keyed-sequence transition contract (open generic, arity 3).</summary>
    public string KeyedChangeInterfaceType { get; init; } = KeyedChangeInterfaceType;

    /// <summary>Single keyed item change contract (open generic, arity 3).</summary>
    public string KeyedItemChangeInterfaceType { get; init; } = KeyedItemChangeInterfaceType;

    /// <summary>Temporary-identity keyed transition contract (open generic, arity 3).</summary>
    public string TemporaryKeyedChangeInterfaceType { get; init; } = TemporaryKeyedChangeInterfaceType;

    /// <summary>Read-only dictionary transition contract (open generic, arity 3).</summary>
    public string DictionaryChangeInterfaceType { get; init; } = DictionaryChangeInterfaceType;

    /// <summary>Single dictionary entry change contract (open generic, arity 2).</summary>
    public string DictionaryItemChangeInterfaceType { get; init; } = DictionaryItemChangeInterfaceType;

    /// <summary>Structural dictionary entry change contract (open generic, arity 3).</summary>
    public string StructuralDictionaryItemChangeInterfaceType { get; init; } = StructuralDictionaryItemChangeInterfaceType;

    /// <summary>Model-independent flattened change record type.</summary>
    public string ChangeRecordType { get; init; } = ChangeRecordType;

    /// <summary>Model-independent change-kind enum type.</summary>
    public string ChangeKindType { get; init; } = ChangeKindType;

    /// <summary>Optional shared scalar-transition type (open generic, arity 1).</summary>
    public string? ScalarTransitionType { get; init; }

    /// <summary>Optional presence-aware scalar-rebase helper.</summary>
    public string? ScalarRebaseType { get; init; }

    /// <summary>Optional helper converting typed Optional endpoints to object-valued endpoints.</summary>
    public string? OptionalBoxType { get; init; }

    /// <summary>Optional collection-state type enabled for generated nested-state queries.</summary>
    public string? CollectionStateType { get; init; }

    /// <summary>Optional non-generic bridge implemented by generated change sets for nested-state queries.</summary>
    public string? CollectionStateQueryInterfaceType { get; init; }

    /// <summary>Optional path type used by generated collection-state query contracts.</summary>
    public string? CollectionStatePathType { get; init; }

    /// <summary>Optional path-segment type used by generated nested collection-item queries.</summary>
    public string? CollectionStatePathSegmentType { get; init; }

    /// <summary>Optional path-segment kind enum used by generated nested collection-item queries.</summary>
    public string? CollectionStatePathSegmentKindType { get; init; }

    /// <summary>Optional permanent-key segment member name.</summary>
    public string? CollectionStateKeySegmentKindMemberName { get; init; }

    /// <summary>Optional temporary-key segment member name.</summary>
    public string? CollectionStateTemporaryKeySegmentKindMemberName { get; init; }

    /// <summary>Optional helper type that converts typed transitions to collection-state views.</summary>
    public string? CollectionStateQueriesType { get; init; }

    /// <summary>Optional keyed transition-to-state helper method name.</summary>
    public string? CollectionStateKeyedMethodName { get; init; }

    /// <summary>Optional dictionary transition-to-state helper method name.</summary>
    public string? CollectionStateDictionaryMethodName { get; init; }
}
