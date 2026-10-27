using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

/// <summary>Partitions model output into responsibility-based generated files.</summary>
internal static class SparseTypesGeneratedFilePartitioner
{
    private const string TypesSuffix = ".Types.g.cs";
    private const string ExtensionsSuffix = ".Extensions.g.cs";
    private const string SerializeSuffix = ".Serialize.g.cs";
    private const string JsonSuffix = ".Json.g.cs";
    private const string GeneratedSuffix = ".Generated.g.cs";

    public static ImmutableArray<SparseGeneratedSource> Partition(
        SparseModelInfo model,
        string? extensionsTypeName,
        string source,
        string generatedNamespace,
        string serializeContainerSuffix,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tree = CSharpSyntaxTree.ParseText(source, path: model.HintName, cancellationToken: cancellationToken);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        var syntaxError = tree.GetDiagnostics(cancellationToken).FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (syntaxError is not null)
        {
            throw new InvalidOperationException("Generated source could not be partitioned: " + syntaxError);
        }

        var typeName = model.Name.TrimStart('@');
        var fileBase = SparseFragmentEmitter.GetPromotedHintName(model, string.Empty, cancellationToken);
        if (fileBase.StartsWith("global__", StringComparison.Ordinal))
        {
            fileBase = fileBase.Substring("global__".Length);
        }

        var output = ImmutableArray.CreateBuilder<SparseGeneratedSource>(5);
        AddFile(output, root, fileBase + TypesSuffix, (_, member) => IsTypeDeclaration(member, typeName));
        if (!string.IsNullOrEmpty(extensionsTypeName))
        {
            AddFile(output, root, fileBase + ExtensionsSuffix, (_, member) => IsTypeDeclaration(member, extensionsTypeName));
        }
        AddFile(
            output,
            root,
            fileBase + SerializeSuffix,
            (namespaceName, member) =>
                IsType(member, namespaceName, generatedNamespace, null)
                && IsTypeDeclaration(member, null)
                && GetTypeName(member).EndsWith(serializeContainerSuffix, StringComparison.Ordinal)
        );
        AddFile(
            output,
            root,
            fileBase + JsonSuffix,
            (namespaceName, member) =>
                IsType(member, namespaceName, generatedNamespace, null)
                && (
                    GetTypeName(member).EndsWith(serializeContainerSuffix + "PayloadJsonConverter", StringComparison.Ordinal)
                    || GetTypeName(member).EndsWith(serializeContainerSuffix + "PayloadJsonResolver", StringComparison.Ordinal)
                )
        );
        AddFile(
            output,
            root,
            fileBase + GeneratedSuffix,
            (namespaceName, member) =>
                IsType(member, namespaceName, generatedNamespace, null)
                && !GetTypeName(member).EndsWith(serializeContainerSuffix, StringComparison.Ordinal)
                && !GetTypeName(member).EndsWith(serializeContainerSuffix + "PayloadJsonConverter", StringComparison.Ordinal)
                && !GetTypeName(member).EndsWith(serializeContainerSuffix + "PayloadJsonResolver", StringComparison.Ordinal)
        );
        return output.ToImmutable();
    }

    private static void AddFile(
        ImmutableArray<SparseGeneratedSource>.Builder output,
        CompilationUnitSyntax original,
        string hintName,
        Func<string?, MemberDeclarationSyntax, bool> include
    )
    {
        var members = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();
        foreach (var member in original.Members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                var namespaceName = namespaceDeclaration.Name?.ToString();
                var namespaceMembers = namespaceDeclaration.Members.Where(member => include(namespaceName, member)).ToArray();
                if (namespaceMembers.Length > 0)
                {
                    namespaceMembers[0] = EnsureSingleLeadingNewLine(namespaceMembers[0]);
                    var partitionedNamespace = namespaceDeclaration.WithMembers(SyntaxFactory.List(namespaceMembers));
                    if (partitionedNamespace is NamespaceDeclarationSyntax blockNamespace)
                    {
                        var openBrace = blockNamespace.OpenBraceToken;
                        partitionedNamespace = blockNamespace.WithOpenBraceToken(
                            openBrace.WithTrailingTrivia(RemoveLeadingWhitespace(openBrace.TrailingTrivia))
                        );
                    }
                    members.Add(partitionedNamespace);
                }
            }
            else if (include(null, member))
            {
                members.Add(member);
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        var partition = SyntaxFactory
            .CompilationUnit()
            .WithExterns(original.Externs)
            .WithUsings(original.Usings)
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(original.GetLeadingTrivia())
            .WithTrailingTrivia(original.GetTrailingTrivia());
        output.Add(new SparseGeneratedSource(hintName, partition.ToFullString()));
    }

    private static MemberDeclarationSyntax EnsureSingleLeadingNewLine(MemberDeclarationSyntax member)
    {
        var leadingTrivia = member.GetLeadingTrivia();
        var firstContent = 0;
        while (
            firstContent < leadingTrivia.Count
            && (leadingTrivia[firstContent].IsKind(SyntaxKind.WhitespaceTrivia) || leadingTrivia[firstContent].IsKind(SyntaxKind.EndOfLineTrivia))
        )
        {
            firstContent++;
        }

        var indentation = leadingTrivia.Take(firstContent).LastOrDefault(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia));
        var normalizedTrivia = ImmutableArray.CreateBuilder<SyntaxTrivia>(leadingTrivia.Count - firstContent + 2);
        normalizedTrivia.Add(SyntaxFactory.EndOfLine("\n"));
        normalizedTrivia.Add(indentation.RawKind == 0 ? SyntaxFactory.Whitespace("    ") : indentation);
        normalizedTrivia.AddRange(leadingTrivia.Skip(firstContent));
        return member.WithLeadingTrivia(SyntaxFactory.TriviaList(normalizedTrivia));
    }

    private static SyntaxTriviaList RemoveLeadingWhitespace(SyntaxTriviaList trivia)
    {
        var firstContent = 0;
        while (
            firstContent < trivia.Count
            && (trivia[firstContent].IsKind(SyntaxKind.WhitespaceTrivia) || trivia[firstContent].IsKind(SyntaxKind.EndOfLineTrivia))
        )
        {
            firstContent++;
        }

        return SyntaxFactory.TriviaList(trivia.Skip(firstContent));
    }

    private static bool IsType(MemberDeclarationSyntax member, string? actualNamespaceName, string expectedNamespaceName, string? typeName)
    {
        return string.Equals(actualNamespaceName ?? string.Empty, expectedNamespaceName, StringComparison.Ordinal)
            && IsTypeDeclaration(member, typeName);
    }

    private static bool IsTypeDeclaration(MemberDeclarationSyntax member, string? typeName)
    {
        if (member is not BaseTypeDeclarationSyntax typeDeclaration)
        {
            return false;
        }

        return typeName is null || string.Equals(typeDeclaration.Identifier.ValueText, typeName, StringComparison.Ordinal);
    }

    private static string GetTypeName(MemberDeclarationSyntax member) =>
        member is BaseTypeDeclarationSyntax typeDeclaration ? typeDeclaration.Identifier.ValueText : string.Empty;
}
