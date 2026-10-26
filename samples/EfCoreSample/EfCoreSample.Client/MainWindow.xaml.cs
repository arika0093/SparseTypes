using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EfCoreSample.Contracts;
using SparseTypes;

namespace EfCoreSample.Client;

/// <summary>
/// The order editor window. The form binds to the live edit session's
/// observable proxy; changed fields are highlighted, history drives
/// undo/redo, and save ships a sparse change payload guarded by the
/// concurrency token observed at read time.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly Brush ChangedBrush = CreateFrozenBrush(0xFF, 0xF3, 0xC4);

    /// <summary>Saves the pending edits (Ctrl+S).</summary>
    public static readonly RoutedUICommand SaveCommand = new(
        "Save",
        "Save",
        typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control) }
    );

    /// <summary>Re-reads the server order (F5).</summary>
    public static readonly RoutedUICommand RefreshCommand = new(
        "Refresh",
        "Refresh",
        typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.F5) }
    );

    private readonly HttpClient _client;
    private OrderDto.EditSession? _session;
    private ISparseEditHistory? _history;
    private HashSet<string> _changedPaths = [];
    private string _orderIdText = "1";
    private int _selectedLineIndex = -1;
    private string _status = string.Empty;
    private string _changeSummary = string.Empty;
    private string _linesHeader = "Lines";
    private bool _isBusy;
    private int _changeVersion;
    private Brush _nameBrush = Brushes.Transparent;
    private Brush _notesBrush = Brushes.Transparent;
    private Brush _codeBrush = Brushes.Transparent;
    private Brush _shipCityBrush = Brushes.Transparent;
    private Brush _streetBrush = Brushes.Transparent;
    private Brush _cityBrush = Brushes.Transparent;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Creates the order editor window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        var serverAddress =
            Environment.GetEnvironmentVariable("services__server__http__0")
            ?? "http://localhost:5000";
        _client = new HttpClient { BaseAddress = new Uri(serverAddress.TrimEnd('/') + "/") };
        Status = $"Server: {_client.BaseAddress} Enter an order id and press Load.";
        var rowStyle = new Style(typeof(DataGridRow));
        var background = new MultiBinding { Converter = new LineRowBrushConverter(() => Session) };
        background.Bindings.Add(new Binding());
        background.Bindings.Add(new Binding(nameof(ChangeVersion)) { Source = this });
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, background));
        LinesGrid.RowStyle = rowStyle;
    }

    /// <summary>Gets or sets the order id text entered in the toolbar.</summary>
    public string OrderIdText
    {
        get => _orderIdText;
        set
        {
            if (_orderIdText != value)
            {
                _orderIdText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the live edit session backing the form, if an order is loaded.</summary>
    public OrderDto.EditSession? Session
    {
        get => _session;
        private set
        {
            if (!ReferenceEquals(_session, value))
            {
                _session = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSession));
            }
        }
    }

    /// <summary>Gets whether an order is loaded.</summary>
    public bool HasSession => Session is not null;

    /// <summary>Gets whether the window is idle and can start work.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Gets whether a load, refresh, or save round trip is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanReset));
                OnPropertyChanged(nameof(CanRemoveLine));
                OnPropertyChanged(nameof(CanInspectChanges));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Gets whether pending edits can be discarded.</summary>
    public bool CanReset => HasSession && IsIdle && (Session?.HasChanges == true);

    /// <summary>Gets whether the pending payload and change table can be viewed.</summary>
    public bool CanInspectChanges => HasSession && IsIdle && (Session?.HasChanges == true);

    /// <summary>Gets whether the selected line can be removed right now.</summary>
    public bool CanRemoveLine => HasSession && IsIdle && SelectedLineIndex >= 0;

    /// <summary>Gets or sets the selected line row index.</summary>
    public int SelectedLineIndex
    {
        get => _selectedLineIndex;
        set
        {
            if (_selectedLineIndex != value)
            {
                _selectedLineIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRemoveLine));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Gets or sets the one-line operation status.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the pending-edit summary shown in the status bar.</summary>
    public string ChangeSummary
    {
        get => _changeSummary;
        private set
        {
            if (_changeSummary != value)
            {
                _changeSummary = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the lines group header with the row count.</summary>
    public string LinesHeader
    {
        get => _linesHeader;
        private set
        {
            if (_linesHeader != value)
            {
                _linesHeader = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Bumps to re-evaluate row highlighting without rebuilding editors.</summary>
    public int ChangeVersion
    {
        get => _changeVersion;
        private set
        {
            if (_changeVersion != value)
            {
                _changeVersion = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the name field background.</summary>
    public Brush NameBrush
    {
        get => _nameBrush;
        private set => SetBrush(ref _nameBrush, value);
    }

    /// <summary>Gets the notes field background.</summary>
    public Brush NotesBrush
    {
        get => _notesBrush;
        private set => SetBrush(ref _notesBrush, value);
    }

    /// <summary>Gets the code field background.</summary>
    public Brush CodeBrush
    {
        get => _codeBrush;
        private set => SetBrush(ref _codeBrush, value);
    }

    /// <summary>Gets the ship city field background.</summary>
    public Brush ShipCityBrush
    {
        get => _shipCityBrush;
        private set => SetBrush(ref _shipCityBrush, value);
    }

    /// <summary>Gets the street field background.</summary>
    public Brush StreetBrush
    {
        get => _streetBrush;
        private set => SetBrush(ref _streetBrush, value);
    }

    /// <summary>Gets the city field background.</summary>
    public Brush CityBrush
    {
        get => _cityBrush;
        private set => SetBrush(ref _cityBrush, value);
    }

    private void SetBrush(ref Brush field, Brush value, [CallerMemberName] string? name = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            OnPropertyChanged(name);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnSessionChanged() => RefreshChangeState();

    private void OnHistoryStateChanged()
    {
        OnPropertyChanged(nameof(CanReset));
        CommandManager.InvalidateRequerySuggested();
    }

    private static Brush BrushFor(bool changed) => changed ? ChangedBrush : Brushes.Transparent;

    private static bool IsLinesPath(string path) =>
        path == "Lines" || path.StartsWith("Lines[", StringComparison.Ordinal);

    private void RefreshChangeState()
    {
        var session = Session;
        _changedPaths = session is null
            ? []
            : new HashSet<string>(
                session.EnumerateChangedPaths().Select(static path => path.ToString()),
                StringComparer.Ordinal
            );
        NameBrush = BrushFor(_changedPaths.Contains("Name"));
        NotesBrush = BrushFor(_changedPaths.Contains("Notes"));
        CodeBrush = BrushFor(_changedPaths.Contains("Code"));
        ShipCityBrush = BrushFor(_changedPaths.Contains("ShipCity"));
        StreetBrush = BrushFor(_changedPaths.Contains("Address.Street"));
        CityBrush = BrushFor(_changedPaths.Contains("Address.City"));
        ChangeVersion++;
        ChangeSummary =
            session is null ? "No order loaded."
            : _changedPaths.Count == 0 ? "No changes."
            : _changedPaths.Count == 1 ? "1 changed field."
            : $"{_changedPaths.Count} changed fields.";
        LinesHeader = session is null ? "Lines" : $"Lines ({session.Observable.Lines.Count})";
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanRemoveLine));
        OnPropertyChanged(nameof(CanInspectChanges));
        CommandManager.InvalidateRequerySuggested();
    }

    private void AdoptSession(OrderDto.EditSession session, string status)
    {
        if (_history is not null)
        {
            _history.StateChanged -= OnHistoryStateChanged;
        }

        _history = session.EnableHistory(new EditHistoryOptions { MaxEntries = 50 });
        _history.StateChanged += OnHistoryStateChanged;
        Session = session;
        SelectedLineIndex = -1;
        Status = status;
        RefreshChangeState();
        OnHistoryStateChanged();
    }

    private void AdoptOrder(OrderDto order, string status)
    {
        // Bind an empty address so the street/city editors always have a target.
        // The baseline is captured afterwards, so this starts clean.
        var draft = order.DeepClone();
        draft.Address ??= new AddressDto();
        AdoptSession(draft.CreateEditSession(OnSessionChanged), status);
    }

    private void OnUndoCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _history?.CanUndo == true && !IsBusy;
        e.Handled = true;
    }

    private void OnUndoExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var history = _history;
        if (history is null || IsBusy)
        {
            return;
        }

        if (!history.TryUndo(out var conflicts))
        {
            Status = conflicts is null
                ? "Undo did not apply."
                : $"Undo did not apply: {string.Join(", ", conflicts.Select(static conflict => conflict.PathText))}.";
        }

        RefreshChangeState();
        OnHistoryStateChanged();
    }

    private void OnRedoCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _history?.CanRedo == true && !IsBusy;
        e.Handled = true;
    }

    private void OnRedoExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var history = _history;
        if (history is null || IsBusy)
        {
            return;
        }

        if (!history.TryRedo(out var conflicts))
        {
            Status = conflicts is null
                ? "Redo did not apply."
                : $"Redo did not apply: {string.Join(", ", conflicts.Select(static conflict => conflict.PathText))}.";
        }

        RefreshChangeState();
        OnHistoryStateChanged();
    }

    private void OnSaveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Session?.HasChanges == true && !IsBusy;
        e.Handled = true;
    }

    private async void OnSaveExecuted(object sender, ExecutedRoutedEventArgs e) =>
        await SaveAsync();

    private void OnRefreshCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Session is not null && !IsBusy;
        e.Handled = true;
    }

    private async void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e) =>
        await RefreshAsync();

    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = $"Loading order {OrderIdText.Trim()}.";
        try
        {
            var order = await GetOrderAsync();
            if (order is not null)
            {
                AdoptOrder(
                    order,
                    $"Loaded order {OrderIdText.Trim()} with {order.Lines.Count} lines."
                );
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        if (!session.TryRevertChanges(out _))
        {
            Status = "The pending edits could not be reverted in place.";
            return;
        }

        Status = "Pending edits discarded.";
        RefreshChangeState();
        OnHistoryStateChanged();
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Creating a new order.";
        try
        {
            var created = await PostOrderAsync();
            if (created is not null)
            {
                AdoptOrder(created, $"Created order {OrderIdText.Trim()} with placeholder values.");
            }
        }
        catch (HttpRequestException exception)
        {
            Status = $"Could not reach the server: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPayloadClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        var submitted = session.CreateChangeSet();
        if (submitted.IsEmpty)
        {
            Status = "No changes to show.";
            return;
        }

        string wire = JsonSerializer.Serialize(
            submitted.ToPayload(),
            OrderDto.PayloadJson.Default
        );

        var pretty = JsonSerializer.Serialize(
            JsonDocument.Parse(wire),
            new JsonSerializerOptions { WriteIndented = true }
        );
        new PayloadDialog(pretty) { Owner = this }.ShowDialog();
    }

    private void OnShowChangesClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        var rows = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatValue = static value => ChangeValueText.Format(value),
            }
        );
        if (rows.Count == 0)
        {
            Status = "No changes to show.";
            return;
        }

        new ChangesDialog(
            $"Pending edits for order {OrderIdText.Trim()} ({rows.Count} rows).",
            rows
        )
        {
            Owner = this,
        }.ShowDialog();
    }

    private void OnAddLineClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        session.Observable.Lines.AddModel(
            new OrderLineDto
            {
                Quantity = 1,
                UnitPrice = 100m,
                TemporaryId = Guid.NewGuid(),
            }
        );
        SelectedLineIndex = session.Observable.Lines.Count - 1;
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        var index = SelectedLineIndex;
        if (index < 0 || index >= session.Observable.Lines.Count)
        {
            Status = "Select a line to remove.";
            return;
        }

        session.Observable.Lines.RemoveAt(index);
        SelectedLineIndex = -1;
    }

    private async Task SaveAsync()
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        try
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch (InvalidOperationException)
        {
            Status = "Fix the line editor value before saving.";
            return;
        }

        var submitted = session.CreateChangeSet();
        if (submitted.IsEmpty)
        {
            Status = "No changes to save.";
            return;
        }

        IsBusy = true;
        try
        {
            await AttemptSaveAsync(submitted, session.Observable.Version, canRetry: true);
        }
        catch (HttpRequestException exception)
        {
            Status = $"Could not reach the server: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AttemptSaveAsync(OrderDto.ChangeSet submitted, Guid version, bool canRetry)
    {
        var session = Session;
        if (session is null)
        {
            return;
        }

        using var response = await PutAsync(submitted.ToPayload(), version);
        if (response.IsSuccessStatusCode)
        {
            var authoritative =
                await response.Content.ReadFromJsonAsync<OrderDto>()
                ?? throw new InvalidOperationException("The server returned no order.");
            if (!session.TryReconcile(submitted, authoritative, out var reconcileError))
            {
                Status = $"Saved, but the session could not be reconciled: {reconcileError}";
            }

            AdoptOrder(authoritative, $"Saved. Order now has {authoritative.Lines.Count} lines.");
            return;
        }

        if (response.StatusCode == HttpStatusCode.PreconditionFailed && canRetry)
        {
            var fresh = await GetOrderAsync();
            if (fresh is null || Session is null)
            {
                return;
            }

            var reload = Session.Reload(fresh);
            RefreshChangeState();
            if (reload.HasConflicts)
            {
                var resolved = await ResolveWithDialogAsync(
                    reload,
                    fresh,
                    "The order changed on the server and overlaps local edits. Choose per field, then save again."
                );
                if (resolved is null)
                {
                    return;
                }

                Status = "Applying the chosen resolutions.";
                await AttemptSaveAsync(resolved, fresh.Version, canRetry: false);
                return;
            }

            Status =
                "Another writer advanced the order. Rebased onto the latest version; retrying save.";
            await AttemptSaveAsync(Session.CreateChangeSet(), fresh.Version, canRetry: false);
            return;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var fresh = await GetOrderAsync();
            if (fresh is null)
            {
                return;
            }

            var rebase = submitted.RebaseOnto(fresh);
            if (!rebase.HasConflicts && canRetry)
            {
                Status = "The server state moved; rebased cleanly. Retrying save.";
                await AttemptSaveAsync(rebase.Rebased, fresh.Version, canRetry: false);
                return;
            }

            var resolved = await ResolveWithDialogAsync(
                rebase,
                fresh,
                "The submitted edits overlap current server values. Choose per field, then save again."
            );
            if (resolved is null)
            {
                return;
            }

            Status = "Applying the chosen resolutions.";
            await AttemptSaveAsync(resolved, fresh.Version, canRetry: false);
            return;
        }

        Status = $"Save failed: {(int)response.StatusCode} {response.StatusCode}.";
    }

    private async Task<OrderDto.ChangeSet?> ResolveWithDialogAsync(
        RebaseResult<OrderDto.ChangeSet> rebase,
        OrderDto fresh,
        string summary
    )
    {
        var resolution = OrderDto.ChangeSet.BeginResolution(rebase, fresh);
        var dialog = new ConflictDialog(summary, rebase.Conflicts, resolution) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Resolved is null)
        {
            Status = "Kept local edits. Resolve the conflicts or refresh and try again.";
            return null;
        }

        return dialog.Resolved;
    }

    private async Task RefreshAsync()
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var fresh = await GetOrderAsync();
            if (fresh is null)
            {
                return;
            }

            if (!session.HasChanges)
            {
                AdoptOrder(fresh, $"Refreshed order {OrderIdText.Trim()}. No local edits.");
                return;
            }

            var reload = session.Reload(fresh);
            RefreshChangeState();
            if (!reload.HasConflicts)
            {
                Status = $"Server state adopted; {_changedPaths.Count} local edits preserved.";
                return;
            }

            var resolved = await ResolveWithDialogAsync(
                reload,
                fresh,
                "The server changed the same fields as the local edits. Choose per field."
            );
            if (resolved is null)
            {
                return;
            }

            // Apply the decisions onto a fresh draft so the baseline stays at
            // the server state and the chosen edits remain pending.
            var draft = fresh.DeepClone();
            var next = draft.CreateEditSession(OnSessionChanged);
            if (!resolved.TryApplyInPlace(draft, out _))
            {
                Status = "The chosen resolutions could not be applied locally.";
                return;
            }

            AdoptSession(next, $"Resolved {reload.Conflicts.Count} conflicts; review and save.");
        }
        catch (HttpRequestException exception)
        {
            Status = $"Could not reach the server: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<OrderDto?> GetOrderAsync()
    {
        if (!int.TryParse(OrderIdText, out var orderId))
        {
            Status = "The order id must be a number.";
            return null;
        }

        try
        {
            var order = await _client.GetFromJsonAsync<OrderDto>($"orders/{orderId}");
            if (order is null)
            {
                Status = $"Order {orderId} returned no content.";
                return null;
            }

            return order;
        }
        catch (HttpRequestException exception)
        {
            Status = $"Could not load order {orderId}: {exception.Message}";
            return null;
        }
    }

    private async Task<OrderDto?> PostOrderAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "orders");
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var created =
            await response.Content.ReadFromJsonAsync<OrderDto>()
            ?? throw new InvalidOperationException("The server returned no order.");
        // The DTO carries no identity; the route id comes from the 201 location.
        var segment = response.Headers.Location?.OriginalString.Split('/').LastOrDefault();
        if (segment is null || !int.TryParse(segment, out var createdId))
        {
            throw new InvalidOperationException("The server did not return the new order id.");
        }

        OrderIdText = createdId.ToString(CultureInfo.InvariantCulture);
        return created;
    }

    private async Task<HttpResponseMessage> PutAsync(OrderDto.ChangePayload payload, Guid version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"orders/{OrderIdText.Trim()}");
        // The token records the version observed at read time and guards the
        // write against a concurrent writer, even for disjoint edits.
        request.Headers.TryAddWithoutValidation("If-Match", version.ToString());
        request.Content = JsonContent.Create(payload, OrderDto.PayloadJson.Default);
        return await _client.SendAsync(request);
    }

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
