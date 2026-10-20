using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator;

/// <summary>Diagnostic descriptors for the standalone generator.</summary>
/// <remarks>Split from <c>SparseTypesGenerator</c> to keep the entry file within the size guideline.</remarks>
public sealed partial class SparseTypesGenerator
{
    private static readonly DiagnosticDescriptor MustBePartial = new(
        SparseTypesDiagnosticIds.MustBePartial,
        "Sparse model must be partial",
        "Model '{0}' must be declared partial",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf001-sparse-model-must-be-partial"
    );
    private static readonly DiagnosticDescriptor UnsupportedModel = new(
        SparseTypesDiagnosticIds.UnsupportedModel,
        "Unsupported sparse model",
        "Model '{0}' must be a top-level, non-generic, non-abstract class or struct",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf002-unsupported-sparse-model"
    );
    private static readonly DiagnosticDescriptor MissingConstructor = new(
        SparseTypesDiagnosticIds.MissingConstructor,
        "Model needs a supported constructor",
        "Class model '{0}' must have a parameterless constructor or a constructor whose parameters match public readable properties by name and type; a setter, when present, must be public",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf003-model-needs-a-supported-constructor"
    );
    private static readonly DiagnosticDescriptor InvalidMergeStrategy = new(
        SparseTypesDiagnosticIds.InvalidMergeStrategy,
        "Invalid custom merge strategy",
        "Merge strategy for member '{0}' must derive from FragmentMergeStrategy<TMember> and be a concrete, accessible type",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf004-invalid-custom-merge-strategy"
    );
    private static readonly DiagnosticDescriptor InvalidRebasePolicy = new(
        SparseTypesDiagnosticIds.InvalidRebasePolicy,
        "Invalid custom rebase policy",
        "Rebase policy for member '{0}' must derive from FragmentRebasePolicy<TMember>, be a concrete, accessible type, and target a scalar or whole-replace member",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf027-invalid-custom-rebase-policy"
    );
    private static readonly DiagnosticDescriptor InvalidComparisonStrategy = new(
        SparseTypesDiagnosticIds.InvalidComparisonStrategy,
        "Invalid equality comparer",
        "Comparison rule for member '{0}' must use a concrete accessible type implementing IEqualityComparer<TMember> with an accessible parameterless constructor",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf030-invalid-comparison-strategy"
    );
    private static readonly DiagnosticDescriptor UnsupportedMerge = new(
        SparseTypesDiagnosticIds.UnsupportedMerge,
        "Unsupported merge mode",
        "The configured merge mode is not supported for member '{0}'",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf005-unsupported-merge-mode"
    );
    private static readonly DiagnosticDescriptor UnsupportedStructural = new(
        SparseTypesDiagnosticIds.UnsupportedStructural,
        "Unsupported structural member construction",
        "Member '{0}' has an unsupported structural type; provide a supported public constructor and properties, decorate it as a fragment model, or explicitly select MergeMode.Replace",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf007-unsupported-structural-member-construction"
    );
    private static readonly DiagnosticDescriptor UnsupportedClone = new(
        SparseTypesDiagnosticIds.UnsupportedClone,
        "Unsupported deep clone member",
        "Member '{0}' has a reference shape that cannot be deeply cloned safely (unsupported type or constructor-bound cycle); use a supported structural type or collection, or explicitly mark a reference-safe property with SparseCloneReferenceSafe",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf008-unsupported-deep-clone-member"
    );

    private static readonly DiagnosticDescriptor GeneratedNameCollision = new(
        SparseTypesDiagnosticIds.GeneratedNameCollision,
        "Member conflicts with generated API",
        "Member '{0}' conflicts with a name reserved by the generated API",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf009-member-conflicts-with-generated-api"
    );

    private static readonly DiagnosticDescriptor UnsupportedRequired = new(
        SparseTypesDiagnosticIds.UnsupportedRequired,
        "Required member cannot be constructed",
        "Required member '{0}' must be represented by an accessible public property in the fragment construction plan",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf006-required-member-cannot-be-constructed"
    );

    private static readonly DiagnosticDescriptor IncompatiblePromotedModel = new(
        SparseTypesDiagnosticIds.IncompatiblePromotedModel,
        "Incompatible promoted fragment model",
        "Promoted model '{0}' requires incompatible generated semantics from different roots",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf010-incompatible-promoted-fragment-model"
    );

    private static readonly DiagnosticDescriptor IncompatibleEntityDefaultMapping = new(
        SparseTypesDiagnosticIds.IncompatibleEntityDefaultMapping,
        "Incompatible entity default mapping",
        "Incompatible conventional entity mapping: {0}",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf033-incompatible-entity-default-mapping"
    );

    private static readonly DiagnosticDescriptor InvalidEntityMapperDeclaration = new(
        SparseTypesDiagnosticIds.InvalidEntityMapperDeclaration,
        "Invalid entity mapper declaration",
        "Invalid entity mapper declaration: {0}",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf034-invalid-entity-mapper-declaration"
    );

    private static readonly DiagnosticDescriptor UnsupportedEntityMemberShape = new(
        SparseTypesDiagnosticIds.UnsupportedEntityMemberShape,
        "Unsupported entity member shape",
        "Unsupported conventional entity member shape: {0}",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf035-unsupported-entity-member-shape"
    );

    private static readonly DiagnosticDescriptor UnkeyedStructuralSequence = new(
        SparseTypesDiagnosticIds.UnkeyedStructuralSequence,
        "Structural sequence without usable key",
        "Member '{0}' is a structural sequence without a usable key; mark exactly one property of the element type with [SparseKey], or explicitly select MergeMode.Replace, MergeMode.Append, MergeMode.SetUnion, or a custom merge strategy",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf011-structural-sequence-without-usable-key"
    );

    private static readonly DiagnosticDescriptor MultiplePropertyKeys = new(
        SparseTypesDiagnosticIds.MultiplePropertyKeys,
        "Multiple SparseKey properties",
        "Type '{0}' marks more than one property with [SparseKey]; declare exactly one [SparseKey] property per keyed element type",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf013-multiple-sparsekey-properties"
    );

    private static readonly DiagnosticDescriptor InvalidKeyAttributeShape = new(
        SparseTypesDiagnosticIds.InvalidKeyAttributeShape,
        "Invalid SparseKey declaration",
        "SparseKey declaration on '{0}' is invalid; [SparseKey] targets a single property and takes no arguments",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf014-invalid-sparsekey-declaration"
    );

    private static readonly DiagnosticDescriptor InaccessibleKeyProperty = new(
        SparseTypesDiagnosticIds.InaccessibleKeyProperty,
        "Inaccessible SparseKey property",
        "Key property '{0}' must be a publicly readable instance property; static, indexer, or non-publicly-readable properties cannot serve as stable identity",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf017-inaccessible-sparsekey-property"
    );

    private static readonly DiagnosticDescriptor UnsupportedKeyShape = new(
        SparseTypesDiagnosticIds.UnsupportedKeyShape,
        "Unsupported SparseKey shape",
        "Key '{0}' has a collection-shaped type; collection-shaped keys are not supported for keyed collection identity",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf019-unsupported-sparsekey-shape"
    );

    private static readonly DiagnosticDescriptor DuplicateJsonPropertyName = new(
        SparseTypesDiagnosticIds.DuplicateJsonPropertyName,
        "Duplicate JSON property name",
        "Multiple members map to the same JSON property name '{0}'",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf021-duplicate-json-property-name"
    );
    private static readonly DiagnosticDescriptor SparseIgnoreOnKey = new(
        SparseTypesDiagnosticIds.SparseIgnoreOnKey,
        "SparseIgnore cannot exclude a key",
        "Property '{0}' is a SparseKey and cannot be ignored",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf022-sparseignore-on-key"
    );
    private static readonly DiagnosticDescriptor SparseIgnoreUnsupportedProperty = new(
        SparseTypesDiagnosticIds.SparseIgnoreUnsupportedProperty,
        "SparseIgnore property cannot be preserved",
        "Property '{0}' cannot be ignored because required, constructor-bound without a default, or init-only properties cannot be carried through model construction",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf023-sparseignore-on-unsupported-property"
    );
    private static readonly DiagnosticDescriptor InvalidUnassignedKey = new(
        SparseTypesDiagnosticIds.InvalidUnassignedKey,
        "Invalid unassigned key sentinel",
        "Invalid unassigned key sentinel: {0}",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf024-invalid-unassigned-key-sentinel"
    );
    private static readonly DiagnosticDescriptor InvalidTemporaryKeyShape = new(
        SparseTypesDiagnosticIds.InvalidTemporaryKeyShape,
        "Invalid SparseTemporaryKey declaration",
        "Invalid temporary-identity declaration: {0}; mark exactly one Guid? property per keyed element type with parameterless [SparseTemporaryKey]",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf031-invalid-sparsetemporarykey-declaration"
    );
    private static readonly DiagnosticDescriptor TemporaryKeyInitializer = new(
        SparseTypesDiagnosticIds.TemporaryKeyInitializer,
        "Temporary key property has an initializer",
        "Temporary-identity property '{0}' has an explicit initializer; assign temporary values in constructors or object initializers instead",
        "SparseTypes",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf032-temporary-key-property-initializer"
    );
    private static readonly DiagnosticDescriptor InPlaceWriteUnavailable = new(
        SparseTypesDiagnosticIds.InPlaceWriteUnavailable,
        "In-place updates are limited",
        "Model '{0}' has init-only or constructor-only members; patch and try-apply APIs report structured failures when changes include them",
        "SparseTypes",
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf026-in-place-submit-is-unavailable"
    );
    private static readonly DiagnosticDescriptor InvalidEmissionPlan = new(
        SparseTypesDiagnosticIds.InvalidEmissionPlan,
        "Invalid downstream emission plan",
        "Invalid emission plan: {0}",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf028-invalid-downstream-emission-plan"
    );
    private static readonly DiagnosticDescriptor UnknownProductMember = new(
        SparseTypesDiagnosticIds.UnknownProductMember,
        "Unknown product member",
        "Product policy references unknown member '{0}'",
        "SparseTypes",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf029-unknown-product-member"
    );
    private static readonly DiagnosticDescriptor UnsupportedPayloadJsonPoco = new(
        SparseTypesDiagnosticIds.UnsupportedPayloadJsonPoco,
        "Custom leaf type needs external JSON metadata",
        "Custom leaf type '{0}' has a shape without a generated payload JSON codec; combine PayloadJson.Default with a source-generated context for this type or simplify it to public properties with an accessible constructor",
        "SparseTypes",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: "https://github.com/arika0093/SparseTypes/blob/main/docs/reference/analyzer.md#spf036-custom-leaf-type-needs-external-json-metadata"
    );
}
