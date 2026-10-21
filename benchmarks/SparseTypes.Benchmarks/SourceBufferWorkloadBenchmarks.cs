using BenchmarkDotNet.Attributes;
using SparseTypes.Generator.Shared;

[MemoryDiagnoser]
public class SourceBufferWorkloadBenchmarks
{
    private string _ordinarySource = null!;

    [Params(4096, 8192)]
    public int LargeRepetitions { get; set; }

    [Params(0, 4, 16)]
    public int OrdinaryGroups { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _ordinarySource = string.Concat(Enumerable.Repeat("    // generated member body\n", 1024));
        if (EmitLarge().Length <= _ordinarySource.Length)
        {
            throw new InvalidOperationException(
                "The large emission must exceed an ordinary group."
            );
        }

        if (EmitOrdinaryGroup() != 4 * _ordinarySource.Length)
        {
            throw new InvalidOperationException(
                "All simultaneous source owners must remain intact."
            );
        }

        if (EmitThenEmit() != EmitLarge().Length + OrdinaryGroups * 4 * _ordinarySource.Length)
        {
            throw new InvalidOperationException(
                "Mixed source workloads must preserve output sizes."
            );
        }
    }

    [Benchmark]
    public int EmitThenEmit()
    {
        var length = EmitLarge().Length;
        for (var group = 0; group < OrdinaryGroups; group++)
        {
            length += EmitOrdinaryGroup();
        }

        return length;
    }

    // A representative large emission through the shared builder: one large
    // source followed by ordinary simultaneous emissions, verifying
    // pooled-buffer retention and ownership without the retired rewrite stage.
    private string EmitLarge()
    {
        using var code = new SharedIndentedBuilder(CancellationToken.None);
        for (var index = 0; index < LargeRepetitions; index++)
        {
            code.AppendLine("var view = global::Ns.Child.Observable.Create();");
        }

        return code.ToString();
    }

    private int EmitOrdinaryGroup()
    {
        using var first = new SharedIndentedBuilder(CancellationToken.None);
        using var second = new SharedIndentedBuilder(CancellationToken.None);
        using var third = new SharedIndentedBuilder(CancellationToken.None);
        using var fourth = new SharedIndentedBuilder(CancellationToken.None);
        first.Append(_ordinarySource);
        second.Append(_ordinarySource);
        third.Append(_ordinarySource);
        fourth.Append(_ordinarySource);
        var a = first.ToString();
        var b = second.ToString();
        var c = third.ToString();
        var d = fourth.ToString();
        if (
            a != _ordinarySource
            || b != _ordinarySource
            || c != _ordinarySource
            || d != _ordinarySource
        )
        {
            throw new InvalidOperationException(
                "Borrowed source buffers must have exclusive owners."
            );
        }

        return a.Length + b.Length + c.Length + d.Length;
    }
}
