using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class WatcherBenchItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class WatcherBenchRoot
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<WatcherBenchItem> Items { get; set; } = new();
}

// Each watcher re-diffs the baseline per edit; setup validates results before
// timing so the benchmark fails on functional drift.
[MemoryDiagnoser]
public class FieldStateWatcherBenchmarks
{
    private const int EditCount = 32;
    private const int RowCount = 64;
    private const int LargeRowCount = 2048;
    private const int FewWatchers = 4;
    private const int ManyWatchers = 64;
    private const int TenWatchers = 10;
    private const int HundredWatchers = 100;
    private const int ThousandWatchers = 1000;

    private WatcherBenchRoot _prototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new WatcherBenchRoot
        {
            Title = "prototype",
            Count = 7,
            Items = Enumerable
                .Range(0, RowCount)
                .Select(index => new WatcherBenchItem { Id = "id-" + index, Name = "n-" + index })
                .ToList(),
        };

        // Validates the workload before timing: watchers observe edits,
        // restore clears, unrelated edits stay silent, disposal stops.
        var model = FreshRoot();
        var session = model.CreateEditSession();
        using (var title = session.ObserveField(WatcherBenchRoot.SparsePath.Title))
        using (var count = session.ObserveField(WatcherBenchRoot.SparsePath.Count))
        {
            if (title.IsChanged || count.IsChanged)
            {
                throw new InvalidOperationException("Watcher setup must start clean.");
            }

            session.Observable.Title = "edited";
            if (!title.IsChanged || count.IsChanged)
            {
                throw new InvalidOperationException("Watcher setup must observe edits.");
            }

            session.Observable.Title = "prototype";
            if (title.IsChanged)
            {
                throw new InvalidOperationException("Watcher setup must clear on restore.");
            }
        }

        session.Observable.Title = "unobserved";
        if (model.Title != "unobserved")
        {
            throw new InvalidOperationException("Watcher setup must apply edits.");
        }
    }

    private WatcherBenchRoot FreshRoot() =>
        new()
        {
            Title = _prototype.Title,
            Count = _prototype.Count,
            Items = _prototype
                .Items.Select(item => new WatcherBenchItem { Id = item.Id, Name = item.Name })
                .ToList(),
        };

    private static WatcherBenchRoot FreshLargeRoot() =>
        new()
        {
            Title = "large",
            Count = 7,
            Items = Enumerable
                .Range(0, LargeRowCount)
                .Select(index => new WatcherBenchItem { Id = "id-" + index, Name = "n-" + index })
                .ToList(),
        };

    private static List<IDisposable> CreateWatchers(
        WatcherBenchRoot.EditSession session,
        int watcherCount
    )
    {
        var watchers = new List<IDisposable>(watcherCount);
        if (watcherCount >= 1)
        {
            watchers.Add(session.ObserveField(WatcherBenchRoot.SparsePath.Title));
        }

        if (watcherCount >= 2)
        {
            watchers.Add(session.ObserveField(WatcherBenchRoot.SparsePath.Count));
        }

        for (var index = 0; index < watcherCount - 2; index++)
        {
            var key = "id-" + (index % RowCount);
            watchers.Add(session.ObserveField(WatcherBenchRoot.SparsePath.Items.Key(key).Name));
        }

        return watchers;
    }

    private static void DisposeWatchers(List<IDisposable> watchers)
    {
        for (var index = 0; index < watchers.Count; index++)
        {
            watchers[index].Dispose();
        }
    }

    [Benchmark(Baseline = true, Description = "32 scalar edits without watchers")]
    public int ScalarEditsWithoutWatcher()
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        for (var index = 0; index < EditCount; index++)
        {
            session.Observable.Title = "edit-" + index;
        }

        return model.Title.Length + model.Count;
    }

    [Benchmark(Description = "32 scalar edits with 4 watchers")]
    public int ScalarEditsWithFewWatchers()
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, FewWatchers);
        try
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }

            return model.Title.Length + model.Count;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }

    [Benchmark(Description = "32 scalar edits with 64 watchers")]
    public int ScalarEditsWithManyWatchers()
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, ManyWatchers);
        try
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }

            return model.Title.Length + model.Count;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }

    [Benchmark(Description = "32 keyed edits with 64 watchers")]
    public int KeyedEditsWithManyWatchers()
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, ManyWatchers);
        try
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Items[index % RowCount].Name = "edit-" + index;
            }

            return model.Items.Count + model.Items[0].Name.Length;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }

    // #318 matrix: one shared dispatch snapshot serves every watcher, so the
    // marginal cost per watcher stays flat while the root grows.
    [Benchmark(Description = "32 scalar edits with 1 watcher")]
    public int ScalarEditsWithOneWatcher() => ScalarEditsWithWatchers(1);

    [Benchmark(Description = "32 scalar edits with 10 watchers")]
    public int ScalarEditsWithTenWatchers() => ScalarEditsWithWatchers(TenWatchers);

    [Benchmark(Description = "32 scalar edits with 100 watchers")]
    public int ScalarEditsWithHundredWatchers() => ScalarEditsWithWatchers(HundredWatchers);

    [Benchmark(Description = "32 scalar edits with 1000 watchers")]
    public int ScalarEditsWithThousandWatchers() => ScalarEditsWithWatchers(ThousandWatchers);

    [Benchmark(Description = "32 scalar edits, 1000 watchers, 2048 rows")]
    public int ScalarEditsLargeRootWithThousandWatchers()
    {
        var model = FreshLargeRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, ThousandWatchers);
        try
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }

            return model.Title.Length + model.Items.Count;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }

    [Benchmark(Description = "One batch of 32 edits with 1000 watchers")]
    public int BatchedEditsWithThousandWatchers()
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, ThousandWatchers);
        try
        {
            session.BatchEdit(() =>
            {
                for (var index = 0; index < EditCount; index++)
                {
                    session.Observable.Title = "edit-" + index;
                }
            });

            return model.Title.Length + model.Count;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }

    private int ScalarEditsWithWatchers(int watcherCount)
    {
        var model = FreshRoot();
        var session = model.CreateEditSession();
        var watchers = CreateWatchers(session, watcherCount);
        try
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }

            return model.Title.Length + model.Count;
        }
        finally
        {
            DisposeWatchers(watchers);
        }
    }
}
