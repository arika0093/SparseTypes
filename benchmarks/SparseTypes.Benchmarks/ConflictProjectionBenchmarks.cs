using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;
using SparseTypes.Blazor;

[SparseModel]
public partial class ConflictBenchCustomer
{
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
}

[SparseModel]
public partial class ConflictBenchRow
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

[SparseModel]
public partial class ConflictBenchSheet
{
    public ConflictBenchCustomer? Customer { get; set; } = new();

    public List<ConflictBenchRow> Lines { get; set; } = new();

    public Dictionary<string, ConflictBenchCustomer> Contacts { get; set; } = new();
}

// Conflict projection (#333): missing keyed rows must resolve through one
// non-throwing walk instead of one throwing rebuild per shortened prefix.
[MemoryDiagnoser]
public class ConflictProjectionBenchmarks
{
    private const int ConflictCount = 64;
    private const int RowCount = 128;

    private ConflictBenchSheet _prototype = null!;
    private ConflictBenchSheet.EditSession _session = null!;
    private EditContext _context = null!;
    private ValidationMessageStore _store = null!;
    private List<SparseConflict> _resolved = null!;
    private List<SparseConflict> _missingKeys = null!;
    private List<SparseConflict> _deepSuffixes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new ConflictBenchSheet
        {
            Customer = new ConflictBenchCustomer { Name = "Ada", Email = "a@example.com" },
            Lines = Enumerable
                .Range(0, RowCount)
                .Select(index => new ConflictBenchRow { Sku = "sku-" + index, Quantity = index })
                .ToList(),
            Contacts = new() { ["billing"] = new ConflictBenchCustomer { Name = "Ada" } },
        };

        _resolved = Enumerable
            .Range(0, ConflictCount)
            .Select(index =>
                Conflict(
                    ConflictBenchSheet.SparsePath.Lines.Key("sku-" + (index % RowCount)).Quantity
                )
            )
            .ToList();
        _missingKeys = Enumerable
            .Range(0, ConflictCount)
            .Select(index =>
                Conflict(ConflictBenchSheet.SparsePath.Lines.Key("missing-" + index).Quantity)
            )
            .ToList();
        _deepSuffixes = Enumerable
            .Range(0, ConflictCount)
            .Select(index =>
                Conflict(ConflictBenchSheet.SparsePath.Contacts.Key("missing-" + (index % 8)).Email)
            )
            .ToList();

        _session = new ConflictBenchSheet
        {
            Customer = _prototype.Customer,
            Lines = _prototype
                .Lines.Select(line => new ConflictBenchRow
                {
                    Sku = line.Sku,
                    Quantity = line.Quantity,
                })
                .ToList(),
            Contacts = new(_prototype.Contacts),
        }.CreateEditSession();
        _context = _session.CreateEditContext();
        _store = _session.CreateValidationStore(_context);
    }

    [IterationCleanup]
    public void ClearStore() => _store.Clear();

    private static SparseConflict Conflict(SparsePath path) =>
        new(
            path,
            SparseConflictKind.Scalar,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            null
        );

    [Benchmark(Baseline = true, Description = "64 resolved keyed paths")]
    public int AddConflicts_ResolvedKeys() => Add(_resolved);

    [Benchmark(Description = "64 missing keyed rows")]
    public int AddConflicts_MissingKeys() => Add(_missingKeys);

    [Benchmark(Description = "64 missing dictionary keys (deep suffix)")]
    public int AddConflicts_DeepSuffixes() => Add(_deepSuffixes);

    private int Add(List<SparseConflict> conflicts)
    {
        // Keep each invocation independent: without this the store grows
        // without bound across the invocations of one iteration.
        _store.Clear();
        return _session.AddConflicts(_store, _context, conflicts);
    }
}
