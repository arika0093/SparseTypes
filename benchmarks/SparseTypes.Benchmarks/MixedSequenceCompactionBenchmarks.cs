using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class MixedSequenceCompactionBenchmarks
{
    [Params(16, 512)]
    public int Size { get; set; }

    private MixedMemberOperation[] _first = null!;
    private MixedMemberOperation[] _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var first = new List<MixedMemberOperation>(Size * 2);
        var second = new List<MixedMemberOperation>(Size * 2);
        for (var index = 0; index < Size; index++)
        {
            var group = "Group" + index;
            first.Add(Blind(group + ".Child"));
            first.Add(Blind("Retained" + index));
            second.Add(Blind(group + ".Child.Meta"));
            second.Add(Blind(group));
        }

        _first = first.ToArray();
        _second = second.ToArray();
        var composed = Compose();
        if (
            !composed.Succeeded
            || composed.Composed.Count != Size * 2
            || composed.Composed[0].Path != "Retained0"
            || composed.Composed[Size].Path != "Group0"
        )
        {
            throw new InvalidOperationException("Mixed compaction must preserve retained and replacement ordering.");
        }
    }

    [Benchmark]
    public MixedSequenceComposition Compose() => MixedChangeAlgebra.ComposeSequences(_first, _second);

    private static MixedMemberOperation Blind(string path) => new(path, MixedHistoryKind.BlindSet, MixedAfterKind.Value);
}
