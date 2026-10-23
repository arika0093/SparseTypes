using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

public sealed class GeneratedOnceObservationCoordinatorTests
{
    private static Dictionary<string, string> GeneratedSources(CSharpCompilation compilation)
    {
        var driver = CSharpGeneratorDriver.Create([new SparseTypesGenerator().AsSourceGenerator()]).RunGenerators(compilation);
        return driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(static group => group.Key, static group => group.First().SourceText.ToString(), StringComparer.Ordinal);
    }

    [Test]
    public void WatcherCoordinatorIsEmittedOnceAndPublicFacadesStayPerModel()
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(
                """
                using SparseTypes;
                [SparseModel]
                public partial class ObservationFirst
                {
                    public string Name { get; set; } = string.Empty;
                }
                [SparseModel]
                public partial class ObservationSecond
                {
                    public int Count { get; set; }
                }
                """,
                path: "Models.cs"
            ),
        };
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SparseObservationOnceProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );

        var sources = GeneratedSources(compilation);
        var coordinatorSources = sources.Where(pair => pair.Key.EndsWith(".ObservationCoordinator.g.cs", StringComparison.Ordinal)).ToArray();
        coordinatorSources.ShouldHaveSingleItem();
        var coordinator = coordinatorSources[0].Value;
        coordinator.ShouldContain("internal sealed class " + SparseGeneratedOnceNames.ObservationCoordinator + "<TView>");
        coordinator.ShouldContain("snapshotQuery(_view)");
        coordinator.ShouldNotContain("global::SparseTypes.");

        var modelSources = sources
            .Where(pair => pair.Key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToArray();
        modelSources.Length.ShouldBe(2);
        foreach (var model in modelSources)
        {
            model.ShouldContain("ObserveField");
            model.ShouldContain("WatchField");
            model.ShouldContain("SparseObservationCoordinator<global::SparseTypes.SparseChangeSetQueryView>");
            model.ShouldNotContain("__observationHandlers");
            model.ShouldNotContain("__ObservationInvalidated");
        }
    }
}
