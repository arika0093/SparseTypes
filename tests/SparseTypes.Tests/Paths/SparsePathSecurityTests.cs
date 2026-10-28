using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests;

public sealed class SparsePathSecurityTests
{
    [Test]
    [Arguments("a.b")]
    [Arguments("a[b")]
    [Arguments("a]b")]
    [Arguments("a\"b")]
    [Arguments("a\\b")]
    [Arguments("$root")]
    [Arguments("a\nb")]
    [Arguments("a\u0001b")]
    [Arguments("a\ud800b")]
    public void MemberFactoriesRejectAmbiguousNames(string name)
    {
        Should.Throw<ArgumentException>(() => SparsePathSegment.Member(name));
        Should.Throw<ArgumentException>(() => SparsePath.Root<Settings>().Member(name));
        Should.Throw<ArgumentException>(() => SparsePath.Root<Settings>().PrependMember(name));
    }

    [Test]
    public void NullAndEmptyStringKeysKeepDistinctWireIdentities()
    {
        var root = SparsePath.Root<Settings>().Member("Items");
        var nullKey = root.Key<string?>(null);
        var emptyKey = root.Key(string.Empty);
        nullKey.ToString().ShouldBe("Items[null]");
        emptyKey.ToString().ShouldBe("Items[\"\"]");
        SparsePath.Parse<Settings>(nullKey.ToString()).ShouldBe(nullKey);
        SparsePath.Parse<Settings>(emptyKey.ToString()).ShouldBe(emptyKey);
        SparsePath.Parse<Settings>(root.Key("null").ToString()).ShouldBe(root.Key("null"));
    }

    [Test]
    [Arguments("0123")]
    [Arguments("00")]
    [Arguments("2147483648")]
    [Arguments("-1")]
    [Arguments("+1")]
    public void NoncanonicalIndexesRemainStringKeys(string text)
    {
        var path = SparsePath.Parse<Settings>("Items[" + text + "]");
        path.ShouldBe(SparsePath.Root<Settings>().Member("Items").Key(text));
        SparsePath.Parse<Settings>("Items[123]").ShouldBe(SparsePath.Root<Settings>().Member("Items").At(123));
    }

    [Test]
    public void AllEscapersAgreeOnControlsAndSurrogates()
    {
        var source = SparsePatchKernelEmitter.RenderHelperSource("Probe", SparsePatchKernelKind.PathKernels);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)TestMetadataReferences.FromFile(path));
        var compilation = CSharpCompilation.Create(
            "PathKernelProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var method = System.Reflection.Assembly
            .Load(stream.ToArray())
            .GetType("Probe.PatchOperationKernels")!
            .GetMethod("EscapeKeySegment", BindingFlags.NonPublic | BindingFlags.Static)!;
        var text = new string(Enumerable.Range(0, 32).Select(static c => (char)c).ToArray()) + "\"\\\ud800x\udfff😀";
        var path = SparsePath.Root<Settings>().Key(text);
        var expected = path.ToString();
        SparseOperationKernels.EscapeKeySegment(text).ShouldBe(expected[1..^1]);
        method.Invoke(null, [text]).ShouldBe(expected[1..^1]);
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(expected)).ShouldBe(expected);
        SparsePath.Parse<Settings>(expected).ShouldBe(path);
    }
}
