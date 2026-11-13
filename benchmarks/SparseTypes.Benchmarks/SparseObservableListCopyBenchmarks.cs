using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class SparseObservableListCopyBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private ICollection _source = null!;
    private string[] _destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        var model = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            model.Add("item-" + index);
        }

        var source = new SparseObservableList<string, string>(model, static (value, _) => value, static view => view, static () => { });
        _source = source;
        _destination = new string[Size];
    }

    [Benchmark]
    public string NonGenericCopyTo()
    {
        _source.CopyTo(_destination, 0);
        return _destination[0];
    }
}
