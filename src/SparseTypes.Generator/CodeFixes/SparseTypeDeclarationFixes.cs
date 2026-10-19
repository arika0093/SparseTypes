using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator.CodeFixes;

/// <summary>Fixes targeting type declarations (partial, abstract, constructors).</summary>
internal static class SparseTypeDeclarationFixes
{
    public static bool TryFindTypeDeclaration(
        SyntaxNode root,
        Diagnostic diagnostic,
        out TypeDeclarationSyntax? declaration
    )
    {
        declaration = null;
        if (diagnostic.Location.SourceTree is null)
            return false;
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        declaration = token
            .Parent?.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();
        return declaration is not null;
    }

    public static async Task<Document> AddPartialAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken
    )
    {
        if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return document;
        var partialToken = SyntaxFactory
            .Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var updated = declaration.AddModifiers(partialToken);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }

    public static async Task<Document> AddPartialToChainAsync(
        Document document,
        TypeDeclarationSyntax mapper,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;
        var updated = root;
        var current = (SyntaxNode?)mapper;
        while (current is not null)
        {
            if (
                current is TypeDeclarationSyntax declaration
                && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            )
            {
                var partialToken = SyntaxFactory
                    .Token(SyntaxKind.PartialKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space);
                updated = updated.ReplaceNode(declaration, declaration.AddModifiers(partialToken));
            }

            current = current.Parent;
        }

        return document.WithSyntaxRoot(updated);
    }

    public static async Task<Document> RemoveAbstractAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken
    )
    {
        var abstractToken = declaration.Modifiers.FirstOrDefault(static modifier =>
            modifier.IsKind(SyntaxKind.AbstractKeyword)
        );
        if (abstractToken == default)
            return document;
        // A static class is abstract and sealed; removing abstract alone is invalid.
        if (declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            return document;
        var updated = declaration.WithModifiers(
            SyntaxFactory.TokenList(
                declaration.Modifiers.Where(static modifier =>
                    !modifier.IsKind(SyntaxKind.AbstractKeyword)
                )
            )
        );
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }

    public static async Task<Document> AddParameterlessConstructorAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken
    )
    {
        if (declaration is not ClassDeclarationSyntax)
            return document;
        if (
            declaration
                .Members.OfType<ConstructorDeclarationSyntax>()
                .Any(static ctor => ctor.ParameterList.Parameters.Count == 0)
        )
            return document;
        var hasRequiredMembers = declaration
            .Members.OfType<PropertyDeclarationSyntax>()
            .Any(static property => property.Modifiers.Any(SyntaxKind.RequiredKeyword));
        var ctor = SyntaxFactory
            .ConstructorDeclaration(declaration.Identifier.ValueText)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block())
            .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.LineFeed))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.LineFeed));
        if (hasRequiredMembers)
        {
            var setsRequired = SyntaxFactory.Attribute(
                SyntaxFactory.ParseName("System.Diagnostics.CodeAnalysis.SetsRequiredMembers")
            );
            ctor = ctor.AddAttributeLists(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(setsRequired))
            );
        }

        var updated = declaration.AddMembers(ctor);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }
}
