using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

/// <summary>Covers key-sentinel literals in generated C# and in the payload schema.</summary>
public sealed class KeySentinelRegressionTests
{
    private static System.Reflection.Assembly Generate(string keyType, string unassigned)
    {
        var source = $$"""
            using SparseTypes;
            using System.Collections.Generic;
            namespace SentinelProbe
            {
                [SparseModel]
                public partial class Item
                {
                    [SparseKey(Unassigned = {{unassigned}})]
                    public {{keyType}} Id { get; set; }
                    public string Name { get; set; } = "";
                }
                [SparseModel]
                public partial class Root
                {
                    public List<Item> Items { get; set; } = new();
                }
            }
            """;
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(static path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SentinelProbe" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, path: "Probe.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);
        generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        updated.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        using var stream = new MemoryStream();
        updated.Emit(stream).Success.ShouldBeTrue();
        return System.Reflection.Assembly.Load(stream.ToArray());
    }

    private static JsonElement FindUnassignedKey(System.Reflection.Assembly assembly)
    {
        var root = assembly.GetType("SentinelProbe.Root")!;
        var helper = root.GetNestedType("PayloadJson")!;
        var bundle = helper.GetProperty("Schema")!.GetValue(null)!;
        var schemas = (System.Collections.IEnumerable)bundle.GetType().GetProperty("OpenApi31Schemas")!.GetValue(bundle)!;
        foreach (var schema in schemas)
        {
            var json = (string)schema.GetType().GetProperty("JsonSchema")!.GetValue(schema)!;
            using var document = JsonDocument.Parse(json);
            if (TryFind(document.RootElement, out var found))
            {
                return found.Clone();
            }
        }

        throw new InvalidOperationException("No unassignedKey in schema.");
    }

    private static bool TryFind(JsonElement element, out JsonElement found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "unassignedKey")
                {
                    found = property.Value;
                    return true;
                }

                if (TryFind(property.Value, out found))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFind(item, out found))
                {
                    return true;
                }
            }
        }

        found = default;
        return false;
    }

    [Test]
    [Arguments("float", "float.NaN", "NaN")]
    [Arguments("float", "float.PositiveInfinity", "Infinity")]
    [Arguments("float", "float.NegativeInfinity", "-Infinity")]
    [Arguments("double", "double.NaN", "NaN")]
    [Arguments("double", "double.PositiveInfinity", "Infinity")]
    [Arguments("double", "double.NegativeInfinity", "-Infinity")]
    public void NonfiniteNumericSentinelGeneratesValidCodeAndSchema(string keyType, string unassigned, string expected)
    {
        var assembly = Generate(keyType, unassigned);
        var sentinel = FindUnassignedKey(assembly);
        // JSON has no NaN/Infinity literals, so the schema carries the token as a string.
        sentinel.ValueKind.ShouldBe(JsonValueKind.String);
        sentinel.GetString().ShouldBe(expected);
    }

}
