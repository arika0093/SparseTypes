using BenchmarkDotNet.Attributes;
using SparseTypes.EntityFrameworkCore.Mapping;

// Path-discovery budgets for Include collection: the collector traversal
// (GetPaths) is measured separately from EF query translation.
[MemoryDiagnoser]
public class EfIncludePathBenchmarks
{
    [Params(16, 64, 256)]
    public int Depth { get; set; }

    private IncludePathCollector _terminalChain = null!;

    private IncludePathCollector _wideTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _terminalChain = new IncludePathCollector();
        for (var index = 0; index < Depth - 1; index++)
        {
            _terminalChain.PushReference("Owned" + index, typeof(object), owned: true);
        }

        _terminalChain.PushReference("Terminal", typeof(object), owned: false);
        _terminalChain.IncludeCurrent();
        for (var index = 0; index < Depth; index++)
        {
            _terminalChain.Pop();
        }

        _wideTree = new IncludePathCollector();
        _wideTree.PushReference("Root", typeof(object), owned: false);
        for (var index = 0; index < Depth * 4; index++)
        {
            _wideTree.PushReference("Child" + index, typeof(object), owned: false);
            if (index % 2 == 0)
            {
                _wideTree.IncludeCurrent();
            }

            _wideTree.Pop();
        }

        _wideTree.Pop();
    }

    [Benchmark(Description = "Include paths terminal chain")]
    public int TerminalChain() => _terminalChain.GetPaths().Count;

    [Benchmark(Description = "Include paths wide tree")]
    public int WideTree() => _wideTree.GetPaths().Count;
}
