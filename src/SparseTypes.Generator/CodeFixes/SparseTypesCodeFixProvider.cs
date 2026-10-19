using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator.CodeFixes;

/// <summary>Provides code fixes for SparseTypes generator diagnostics.</summary>
/// <remarks>
/// The generator assembly is packed into <c>analyzers/dotnet/cs</c>, so this
/// provider ships with the same package and lights up wherever the generator runs.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SparseTypesCodeFixProvider)), Shared]
public sealed class SparseTypesCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(
            SparseTypesDiagnosticIds.MustBePartial,
            SparseTypesDiagnosticIds.UnsupportedModel,
            SparseTypesDiagnosticIds.MissingConstructor,
            SparseTypesDiagnosticIds.UnsupportedMerge,
            SparseTypesDiagnosticIds.UnsupportedRequired,
            SparseTypesDiagnosticIds.UnsupportedStructural,
            SparseTypesDiagnosticIds.UnsupportedClone,
            SparseTypesDiagnosticIds.UnkeyedStructuralSequence,
            SparseTypesDiagnosticIds.MultiplePropertyKeys,
            SparseTypesDiagnosticIds.InvalidKeyAttributeShape,
            SparseTypesDiagnosticIds.InaccessibleKeyProperty,
            SparseTypesDiagnosticIds.SparseIgnoreOnKey,
            SparseTypesDiagnosticIds.SparseIgnoreUnsupportedProperty,
            SparseTypesDiagnosticIds.InvalidUnassignedKey,
            SparseTypesDiagnosticIds.InvalidTemporaryKeyShape,
            SparseTypesDiagnosticIds.TemporaryKeyInitializer,
            SparseTypesDiagnosticIds.InvalidEntityMapperDeclaration
        );

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.FirstOrDefault(static candidate =>
            candidate.Id.StartsWith("SPF", System.StringComparison.Ordinal)
        );
        if (diagnostic is null || diagnostic.Location.SourceTree is null)
            return;
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
            return;

        switch (diagnostic.Id)
        {
            case SparseTypesDiagnosticIds.MustBePartial:
                RegisterAddPartial(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.UnsupportedModel:
                RegisterRemoveAbstract(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.MissingConstructor:
                RegisterAddConstructor(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.UnsupportedMerge:
                RegisterRemoveMerge(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.UnsupportedRequired:
                RegisterRemoveRequired(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.UnsupportedStructural:
            case SparseTypesDiagnosticIds.UnkeyedStructuralSequence:
                RegisterAddMergeReplace(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.UnsupportedClone:
                RegisterAddCloneSafe(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.MultiplePropertyKeys:
                RegisterKeepFirstKey(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.InvalidKeyAttributeShape:
                RegisterFixKeyShape(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.InaccessibleKeyProperty:
                RegisterMakePublic(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.SparseIgnoreOnKey:
            case SparseTypesDiagnosticIds.SparseIgnoreUnsupportedProperty:
                RegisterRemoveIgnore(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.InvalidUnassignedKey:
                RegisterRemoveUnassigned(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.InvalidTemporaryKeyShape:
                RegisterFixTemporaryKey(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.TemporaryKeyInitializer:
                RegisterRemoveInitializer(context, root, diagnostic);
                break;
            case SparseTypesDiagnosticIds.InvalidEntityMapperDeclaration:
                RegisterFixMapper(context, root, diagnostic);
                break;
        }
    }

    private static void RegisterAddPartial(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (
            !SparseTypeDeclarationFixes.TryFindTypeDeclaration(
                root,
                diagnostic,
                out var declaration
            )
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add 'partial' modifier",
                cancellationToken =>
                    SparseTypeDeclarationFixes.AddPartialAsync(
                        context.Document,
                        declaration!,
                        cancellationToken
                    ),
                "AddPartial_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterRemoveAbstract(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (
            !SparseTypeDeclarationFixes.TryFindTypeDeclaration(
                root,
                diagnostic,
                out var declaration
            )
        )
            return;
        if (!declaration!.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return;
        if (declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove 'abstract' modifier",
                cancellationToken =>
                    SparseTypeDeclarationFixes.RemoveAbstractAsync(
                        context.Document,
                        declaration,
                        cancellationToken
                    ),
                "RemoveAbstract_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterAddConstructor(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (
            !SparseTypeDeclarationFixes.TryFindTypeDeclaration(
                root,
                diagnostic,
                out var declaration
            )
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add parameterless constructor",
                cancellationToken =>
                    SparseTypeDeclarationFixes.AddParameterlessConstructorAsync(
                        context.Document,
                        declaration!,
                        cancellationToken
                    ),
                "AddCtor_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterRemoveMerge(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (!SparseMemberAttributeFixes.TryFindAttribute(property!, "SparseMerge", out _))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove unsupported merge mode",
                cancellationToken =>
                    SparseMemberAttributeFixes.RemoveMergeAttributeAsync(
                        context.Document,
                        property!,
                        cancellationToken
                    ),
                "RemoveMerge_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterRemoveRequired(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (!property!.Modifiers.Any(SyntaxKind.RequiredKeyword))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove 'required' modifier",
                cancellationToken =>
                    SparseMemberAttributeFixes.RemoveRequiredModifierAsync(
                        context.Document,
                        property,
                        cancellationToken
                    ),
                "RemoveRequired_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterAddMergeReplace(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Use MergeMode.Replace",
                cancellationToken =>
                    SparseMemberAttributeFixes.AddMergeReplaceAsync(
                        context.Document,
                        property!,
                        cancellationToken
                    ),
                "MergeReplace_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterAddCloneSafe(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Mark reference-safe for cloning",
                cancellationToken =>
                    SparseMemberAttributeFixes.AddCloneReferenceSafeAsync(
                        context.Document,
                        property!,
                        cancellationToken
                    ),
                "CloneSafe_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterKeepFirstKey(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (
            !SparseTypeDeclarationFixes.TryFindTypeDeclaration(
                root,
                diagnostic,
                out var declaration
            )
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Keep first [SparseKey] and remove the rest",
                cancellationToken =>
                    SparseMemberAttributeFixes.KeepFirstKeyAttributeAsync(
                        context.Document,
                        declaration!,
                        "SparseKey",
                        cancellationToken
                    ),
                "KeepFirstKey_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterFixKeyShape(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        // Type-level [SparseKey] points at the attribute itself: remove it.
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var attribute = token.Parent?.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (
            attribute is not null
            && attribute.Parent is AttributeListSyntax list
            && list.Parent is TypeDeclarationSyntax
        )
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove invalid [SparseKey] declaration",
                    cancellationToken =>
                        SparseMemberAttributeFixes.RemoveAttributeAsync(
                            context.Document,
                            attribute,
                            cancellationToken
                        ),
                    "RemoveKey_" + diagnostic.Id
                ),
                diagnostic
            );
            return;
        }

        // Property-level with arguments: strip the argument list.
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (
            !SparseMemberAttributeFixes.TryFindAttribute(
                property!,
                "SparseKey",
                out var keyAttribute
            ) || keyAttribute?.ArgumentList is null
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Use parameterless [SparseKey]",
                cancellationToken =>
                    SparseMemberAttributeFixes.StripAttributeArgumentsAsync(
                        context.Document,
                        keyAttribute,
                        cancellationToken
                    ),
                "StripKeyArgs_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterMakePublic(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (property!.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Make key property publicly readable",
                cancellationToken =>
                    SparseMemberAttributeFixes.MakePropertyPublicAsync(
                        context.Document,
                        property,
                        cancellationToken
                    ),
                "MakePublic_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterRemoveIgnore(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (!SparseMemberAttributeFixes.TryFindAttribute(property!, "SparseIgnore", out _))
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove [SparseIgnore]",
                cancellationToken =>
                    SparseMemberAttributeFixes.RemoveSparseIgnoreAsync(
                        context.Document,
                        property!,
                        cancellationToken
                    ),
                "RemoveIgnore_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterRemoveUnassigned(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (
            !SparseMemberAttributeFixes.TryFindAttribute(property!, "SparseKey", out var attribute)
            || attribute?.ArgumentList is null
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove invalid Unassigned sentinel",
                cancellationToken =>
                    SparseMemberAttributeFixes.RemoveUnassignedArgumentAsync(
                        context.Document,
                        attribute,
                        cancellationToken
                    ),
                "RemoveUnassigned_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterFixTemporaryKey(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        var message = diagnostic.GetMessage();
        if (SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
        {
            if (
                message.Contains("is ignored")
                && SparseMemberAttributeFixes.TryFindAttribute(
                    property!,
                    "SparseIgnore",
                    out var ignore
                )
                && ignore is not null
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove [SparseIgnore] from temporary key",
                        cancellationToken =>
                            SparseMemberAttributeFixes.RemoveAttributeAsync(
                                context.Document,
                                ignore,
                                cancellationToken
                            ),
                        "RemoveTempIgnore_" + diagnostic.Id
                    ),
                    diagnostic
                );
                return;
            }

            if (
                message.Contains("takes no arguments")
                && SparseMemberAttributeFixes.TryFindAttribute(
                    property!,
                    "SparseTemporaryKey",
                    out var withArgs
                )
                && withArgs?.ArgumentList is not null
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Use parameterless [SparseTemporaryKey]",
                        cancellationToken =>
                            SparseMemberAttributeFixes.StripAttributeArgumentsAsync(
                                context.Document,
                                withArgs,
                                cancellationToken
                            ),
                        "StripTempArgs_" + diagnostic.Id
                    ),
                    diagnostic
                );
                return;
            }

            if (
                message.Contains("cannot combine both key attributes")
                && SparseMemberAttributeFixes.TryFindAttribute(
                    property!,
                    "SparseTemporaryKey",
                    out var combined
                )
                && combined is not null
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove [SparseTemporaryKey] (keep [SparseKey])",
                        cancellationToken =>
                            SparseMemberAttributeFixes.RemoveAttributeAsync(
                                context.Document,
                                combined,
                                cancellationToken
                            ),
                        "RemoveTempCombined_" + diagnostic.Id
                    ),
                    diagnostic
                );
            }

            return;
        }

        // Element-level (multiple marks): keep the first declaration.
        if (
            SparseTypeDeclarationFixes.TryFindTypeDeclaration(root, diagnostic, out var declaration)
            && declaration is not null
        )
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Keep first [SparseTemporaryKey] and remove the rest",
                    cancellationToken =>
                        SparseMemberAttributeFixes.KeepFirstKeyAttributeAsync(
                            context.Document,
                            declaration,
                            "SparseTemporaryKey",
                            cancellationToken
                        ),
                    "KeepFirstTemp_" + diagnostic.Id
                ),
                diagnostic
            );
        }
    }

    private static void RegisterRemoveInitializer(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!SparseMemberAttributeFixes.TryFindProperty(root, diagnostic, out var property))
            return;
        if (property!.Initializer is null)
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove temporary key initializer",
                cancellationToken =>
                    SparseMemberAttributeFixes.RemovePropertyInitializerAsync(
                        context.Document,
                        property,
                        cancellationToken
                    ),
                "RemoveTempInit_" + diagnostic.Id
            ),
            diagnostic
        );
    }

    private static void RegisterFixMapper(
        CodeFixContext context,
        SyntaxNode root,
        Diagnostic diagnostic
    )
    {
        if (!diagnostic.GetMessage().Contains("partial"))
            return;
        if (
            !SparseTypeDeclarationFixes.TryFindTypeDeclaration(
                root,
                diagnostic,
                out var declaration
            )
        )
            return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add 'partial' to mapper chain",
                cancellationToken =>
                    SparseTypeDeclarationFixes.AddPartialToChainAsync(
                        context.Document,
                        declaration!,
                        cancellationToken
                    ),
                "AddMapperPartial_" + diagnostic.Id
            ),
            diagnostic
        );
    }
}
