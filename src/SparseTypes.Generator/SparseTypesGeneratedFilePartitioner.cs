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
    private const int TypesPartition = 0;
    private const int ExtensionsPartition = 1;
    private const int SerializePartition = 2;
    private const int JsonPartition = 3;
    private const int GeneratedPartition = 4;

    private const string TypesSuffix = ".Types.g.cs";
    private const string ExtensionsSuffix = ".Extensions.g.cs";
    private const string SerializeSuffix = ".Serialize.g.cs";
    private const string JsonSuffix = ".Json.g.cs";
    private const string GeneratedSuffix = ".Generated.g.cs";

    private readonly record struct PartitionCriteria
    {
        public PartitionCriteria(string typeName, string? extensionsTypeName, string generatedNamespace, string serializeContainerSuffix)
        {
            TypeName = typeName;
            ExtensionsTypeName = extensionsTypeName;
            GeneratedNamespace = generatedNamespace;
            SerializeContainerSuffix = serializeContainerSuffix;
            JsonConverterSuffix = serializeContainerSuffix + "PayloadJsonConverter";
            JsonResolverSuffix = serializeContainerSuffix + "PayloadJsonResolver";
        }

        public string TypeName { get; init; }

        public string? ExtensionsTypeName { get; init; }

        public string GeneratedNamespace { get; init; }

        public string SerializeContainerSuffix { get; init; }

        public string JsonConverterSuffix { get; init; }

        public string JsonResolverSuffix { get; init; }
    }

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
        var partitions = new List<MemberDeclarationSyntax>?[5];
        var criteria = new PartitionCriteria(typeName, extensionsTypeName, generatedNamespace, serializeContainerSuffix);
        foreach (var member in root.Members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                var namespacePartitions = new List<MemberDeclarationSyntax>?[5];
                var namespaceName = namespaceDeclaration.Name?.ToString();
                foreach (var namespaceMember in namespaceDeclaration.Members)
                {
                    AddMemberToPartitions(namespacePartitions, namespaceMember, GetPartitionMask(namespaceMember, namespaceName, criteria));
                }

                for (var partitionIndex = 0; partitionIndex < namespacePartitions.Length; partitionIndex++)
                {
                    var namespaceMembers = namespacePartitions[partitionIndex];
                    if (namespaceMembers is null)
                    {
                        continue;
                    }

                    namespaceMembers[0] = EnsureSingleLeadingNewLine(namespaceMembers[0]);
                    var partitionedNamespace = namespaceDeclaration.WithMembers(SyntaxFactory.List(namespaceMembers));
                    if (partitionedNamespace is NamespaceDeclarationSyntax blockNamespace)
                    {
                        var openBrace = blockNamespace.OpenBraceToken;
                        partitionedNamespace = blockNamespace.WithOpenBraceToken(
                            openBrace.WithTrailingTrivia(RemoveLeadingWhitespace(openBrace.TrailingTrivia))
                        );
                    }

                    AddMemberToPartitions(partitions, partitionedNamespace, 1 << partitionIndex);
                }
            }
            else
            {
                AddMemberToPartitions(partitions, member, GetPartitionMask(member, null, criteria));
            }
        }

        AddFile(output, root, fileBase + TypesSuffix, partitions[TypesPartition]);
        if (!string.IsNullOrEmpty(extensionsTypeName))
        {
            AddFile(output, root, fileBase + ExtensionsSuffix, partitions[ExtensionsPartition]);
        }

        AddFile(output, root, fileBase + SerializeSuffix, partitions[SerializePartition]);
        AddFile(output, root, fileBase + JsonSuffix, partitions[JsonPartition]);
        AddFile(output, root, fileBase + GeneratedSuffix, partitions[GeneratedPartition]);
        return output.ToImmutable();
    }

    private static int GetPartitionMask(MemberDeclarationSyntax member, string? actualNamespaceName, PartitionCriteria criteria)
    {
        var typeDeclaration = member as BaseTypeDeclarationSyntax;
        var isType = typeDeclaration is not null;
        var memberName = typeDeclaration?.Identifier.ValueText ?? string.Empty;
        var mask = 0;
        if (isType && string.Equals(memberName, criteria.TypeName, StringComparison.Ordinal))
        {
            mask |= 1 << TypesPartition;
        }

        if (
            isType
            && !string.IsNullOrEmpty(criteria.ExtensionsTypeName)
            && string.Equals(memberName, criteria.ExtensionsTypeName, StringComparison.Ordinal)
        )
        {
            mask |= 1 << ExtensionsPartition;
        }

        if (!isType || !string.Equals(actualNamespaceName ?? string.Empty, criteria.GeneratedNamespace, StringComparison.Ordinal))
        {
            return mask;
        }

        var isSerialize = memberName.EndsWith(criteria.SerializeContainerSuffix, StringComparison.Ordinal);
        var isJson =
            memberName.EndsWith(criteria.JsonConverterSuffix, StringComparison.Ordinal)
            || memberName.EndsWith(criteria.JsonResolverSuffix, StringComparison.Ordinal);
        if (isSerialize)
        {
            mask |= 1 << SerializePartition;
        }

        if (isJson)
        {
            mask |= 1 << JsonPartition;
        }

        if (!isSerialize && !isJson)
        {
            mask |= 1 << GeneratedPartition;
        }

        return mask;
    }

    private static void AddMemberToPartitions(List<MemberDeclarationSyntax>?[] partitions, MemberDeclarationSyntax member, int partitionMask)
    {
        for (var partitionIndex = 0; partitionIndex < partitions.Length; partitionIndex++)
        {
            if ((partitionMask & (1 << partitionIndex)) == 0)
            {
                continue;
            }

            var partition = partitions[partitionIndex] ??= new List<MemberDeclarationSyntax>();
            partition.Add(member);
        }
    }

    private static void AddFile(
        ImmutableArray<SparseGeneratedSource>.Builder output,
        CompilationUnitSyntax original,
        string hintName,
        List<MemberDeclarationSyntax>? members
    )
    {
        if (members is null || members.Count == 0)
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
}
