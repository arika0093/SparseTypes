using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator.CodeFixes;

/// <summary>Fixes targeting members and their attributes.</summary>
internal static class SparseMemberAttributeFixes
{
    public static bool TryFindProperty(SyntaxNode root, Diagnostic diagnostic, out PropertyDeclarationSyntax? property)
    {
        property = null;
        if (diagnostic.Location.SourceTree is null)
        {
            return false;
        }

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        property = token.Parent?.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        return property is not null;
    }

    public static bool TryFindAttribute(PropertyDeclarationSyntax property, string attributeShortName, out AttributeSyntax? attribute)
    {
        attribute = null;
        foreach (var list in property.AttributeLists)
        {
            foreach (var candidate in list.Attributes)
            {
                var name = candidate.Name.ToString();
                if (
                    name == attributeShortName
                    || name.EndsWith("." + attributeShortName, System.StringComparison.Ordinal)
                    || name == attributeShortName + "Attribute"
                )
                {
                    attribute = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public static async Task<Document> AddMergeReplaceAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (TryFindAttribute(property, "SparseMerge", out _))
        {
            return document;
        }

        var merge = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("global::SparseTypes.SparseMerge"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression("global::SparseTypes.MergeMode.Replace"))
                )
            )
        );
        var updated = property.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(merge)));
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    public static async Task<Document> AddCloneReferenceSafeAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (TryFindAttribute(property, "SparseCloneReferenceSafe", out _))
        {
            return document;
        }

        var marker = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::SparseTypes.SparseCloneReferenceSafe"));
        var updated = property.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(marker)));
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    public static async Task<Document> RemoveAttributeAsync(Document document, AttributeSyntax attribute, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (attribute.Parent is not AttributeListSyntax list)
        {
            return document;
        }

        if (list.Attributes.Count == 1)
        {
            // The whole list decorates a member; drop the list itself.
            if (list.Parent is MemberDeclarationSyntax member)
            {
                var withoutList = member.RemoveNode(list, SyntaxRemoveOptions.KeepNoTrivia);
                if (withoutList is not null)
                {
                    return document.WithSyntaxRoot(root.ReplaceNode(member, withoutList));
                }
            }

            return document;
        }

        var withoutAttribute = list.RemoveNode(attribute, SyntaxRemoveOptions.KeepNoTrivia);
        if (withoutAttribute is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(list, withoutAttribute));
    }

    public static async Task<Document> RemoveMergeAttributeAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (!TryFindAttribute(property, "SparseMerge", out var attribute) || attribute is null)
        {
            return document;
        }

        return await RemoveAttributeAsync(document, attribute, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> RemoveSparseIgnoreAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (!TryFindAttribute(property, "SparseIgnore", out var attribute) || attribute is null)
        {
            return document;
        }

        return await RemoveAttributeAsync(document, attribute, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> StripAttributeArgumentsAsync(Document document, AttributeSyntax attribute, CancellationToken cancellationToken)
    {
        if (attribute.ArgumentList is null)
        {
            return document;
        }

        var updated = attribute.WithArgumentList(null);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(attribute, updated));
    }

    public static async Task<Document> RemoveUnassignedArgumentAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken
    )
    {
        if (attribute.ArgumentList is null)
        {
            return document;
        }

        var remaining = attribute.ArgumentList.Arguments.Where(static argument => !argument.ToString().Contains("Unassigned"));
        AttributeSyntax updated = remaining.Any()
            ? attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(remaining)))
            : attribute.WithArgumentList(null);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(attribute, updated));
    }

    public static async Task<Document> RemovePropertyInitializerAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (property.Initializer is null)
        {
            return document;
        }

        var updated = property.WithInitializer(null).WithSemicolonToken(default);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    public static async Task<Document> RemoveRequiredModifierAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (!property.Modifiers.Any(SyntaxKind.RequiredKeyword))
        {
            return document;
        }

        var updated = property.WithModifiers(
            SyntaxFactory.TokenList(property.Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.RequiredKeyword)))
        );
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    public static async Task<Document> MakePropertyPublicAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken cancellationToken
    )
    {
        if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return document;
        }

        var modifiers = property.Modifiers;
        var withoutAccess = modifiers.Where(static modifier =>
            !modifier.IsKind(SyntaxKind.PublicKeyword)
            && !modifier.IsKind(SyntaxKind.PrivateKeyword)
            && !modifier.IsKind(SyntaxKind.InternalKeyword)
            && !modifier.IsKind(SyntaxKind.ProtectedKeyword)
        );
        var updated = property.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)).AddRange(withoutAccess));
        AccessorDeclarationSyntax? getter = property.AccessorList?.Accessors.FirstOrDefault(static accessor =>
            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
        );
        if (getter?.Modifiers.Any() == true && property.AccessorList is not null)
        {
            var accessorList = property.AccessorList;
            var accessors = accessorList.Accessors.Select(accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration) ? accessor.WithModifiers(SyntaxFactory.TokenList()) : accessor
            );
            updated = updated.WithAccessorList(accessorList.WithAccessors(SyntaxFactory.List(accessors)));
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    public static async Task<Document> KeepFirstKeyAttributeAsync(
        Document document,
        TypeDeclarationSyntax keyOwner,
        string attributeShortName,
        CancellationToken cancellationToken
    )
    {
        var matches = keyOwner
            .Members.OfType<PropertyDeclarationSyntax>()
            .SelectMany(static property => property.AttributeLists.SelectMany(static list => list.Attributes))
            .Where(attribute =>
            {
                var name = attribute.Name.ToString();
                return name == attributeShortName
                    || name.EndsWith("." + attributeShortName, System.StringComparison.Ordinal)
                    || name == attributeShortName + "Attribute";
            })
            .Skip(1)
            .ToArray();
        if (matches.Length == 0)
        {
            return document;
        }

        var current = document;
        foreach (var match in matches)
        {
            current = await RemoveAttributeAsync(current, match, cancellationToken).ConfigureAwait(false);
            // Refresh the node reference after each edit.
            var root = await current.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return current;
            }
        }

        return current;
    }
}
