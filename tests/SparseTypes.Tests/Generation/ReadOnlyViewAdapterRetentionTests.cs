using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

[SparseModel]
public partial class RetentionBook
{
    public List<RetentionRow> Rows { get; set; } = new();

    public Dictionary<string, int>? Counts { get; set; } = new();

    public List<int> @class { get; set; } = new();
}

[SparseModel]
public partial class RetentionRow
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Per-member adapter retention in generated read-only views.</summary>
/// <remarks>Runtime checks go through a retained session Current view; the last case asserts the emitted shape directly.</remarks>
public sealed class ReadOnlyViewAdapterRetentionTests
{
    private static RetentionBook Book() =>
        new()
        {
            Rows = [new RetentionRow { Name = "first" }, new RetentionRow { Name = "second" }],
            Counts = new() { ["a"] = 1 },
        };

    [Test]
    public void RepeatedReadsReuseAdapterForUnchangedSource()
    {
        var book = Book();
        var view = new RetentionBook.ReadOnlyView(book);

        var first = view.Rows;
        var second = view.Rows;

        ReferenceEquals(first, second).ShouldBeTrue();
        var dictionaryFirst = view.Counts;
        ReferenceEquals(dictionaryFirst, view.Counts).ShouldBeTrue();
    }

    [Test]
    public void InPlaceMutationStaysVisibleThroughRetainedAdapter()
    {
        var book = Book();
        var view = new RetentionBook.ReadOnlyView(book);
        var adapter = view.Rows;
        ReferenceEquals(adapter, view.Rows).ShouldBeTrue();

        book.Rows.Add(new RetentionRow { Name = "third" });

        ReferenceEquals(view.Rows, adapter).ShouldBeTrue();
        view.Rows.Count.ShouldBe(3);
        view.Rows[2].Name.ShouldBe("third");
    }

    [Test]
    public void ReplacedSourceCreatesReplacementAdapterOnRead()
    {
        var book = Book();
        var view = new RetentionBook.ReadOnlyView(book);
        var captured = view.Rows;

        var replacement = new List<RetentionRow> { new() { Name = "next" } };
        book.Rows = replacement;
        view.Rows.ShouldNotBeSameAs(captured);

        // The caller-retained adapter still views the collection it was
        // created for; mutations of the new source never leak into it.
        replacement.Add(new RetentionRow { Name = "extra" });
        captured.Count.ShouldBe(2);

        var afterChange = view.Rows;
        afterChange.Count.ShouldBe(2);
        afterChange[1].Name.ShouldBe("extra");

        // Unchanged reference keeps the replacement cached too.
        ReferenceEquals(view.Rows, afterChange).ShouldBeTrue();
    }

    [Test]
    public void NullSourceClearsCacheAndReturnsNull()
    {
        var book = Book();
        var view = new RetentionBook.ReadOnlyView(book);
        view.Counts.ShouldNotBeNull();

        book.Counts = null;
        view.Counts.ShouldBeNull();

        // A null follow-up does not surface a stale adapter.
        view.Counts.ShouldBeNull();

        book.Counts = new Dictionary<string, int>();
        var restored = view.Counts;
        restored.ShouldNotBeNull();
        ReferenceEquals(restored, view.Counts).ShouldBeTrue();
    }

    [Test]
    public void NullableCollectionReturnsNullWithoutAdapter()
    {
        var view = new RetentionBook.ReadOnlyView(new RetentionBook { Counts = null });
        view.Counts.ShouldBeNull();
        view.Rows.ShouldNotBeNull();
    }

    [Test]
    public void EmissionUsesAtomicCacheHoldersAndEscapesKeywordProperties()
    {
        var perModel = GeneratedSources()
            .Where(static pair => pair.Key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .Where(static source =>
                source.Contains("__sparse_cache_Rows", StringComparison.Ordinal)
            )
            .ToArray();
        perModel.ShouldHaveSingleItem();
        var text = perModel[0];
        text.ShouldContain("__sparse_cache_Counts");
        text.ShouldContain("__SparseReadOnlyCache_Rows");
        text.ShouldContain("ReferenceEquals(cached.Source, current)");
        text.ShouldContain("cached.Adapter");
        text.ShouldContain("__sparse_cache_class");
        text.ShouldNotContain("__sparse_cache_@class");
    }

    private static Dictionary<string, string> GeneratedSources()
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(
                """
                using SparseTypes;
                using System.Collections.Generic;
                [SparseModel]
                public partial class RetentionBook
                {
                    public List<RetentionRow> Rows { get; set; } = new();
                    public Dictionary<string, int>? Counts { get; set; } = new();
                    public List<int> @class { get; set; } = new();
                }
                [SparseModel]
                public partial class RetentionRow
                {
                    [SparseKey]
                    public string Name { get; set; } = string.Empty;
                }
                """,
                path: "Models.cs"
            ),
        };
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "RetentionEmissionProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var driver = CSharpGeneratorDriver
            .Create([new SparseTypesGenerator().AsSourceGenerator()])
            .RunGenerators(compilation);
        var generated = driver.GetRunResult();
        return generated
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().SourceText.ToString(),
                StringComparer.Ordinal
            );
    }
}
