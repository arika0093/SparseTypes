using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using EfCoreSample.Contracts;

namespace EfCoreSample.Client;

/// <summary>
/// The order editor window. The form binds to the live edit session's
/// observable proxy, while save ships a sparse change payload guarded by
/// the concurrency token observed at read time.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly HttpClient _client;
    private OrderDto.EditSession? _session;
    private string _orderIdText = "1";
    private int _selectedLineIndex = -1;
    private string _status = "Enter an order id and press Load.";
    private string _conflictsText = string.Empty;
    private bool _isBusy;

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
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanRemoveLine));
            }
        }
    }

    /// <summary>Gets whether an order is loaded.</summary>
    public bool HasSession => Session is not null;

    /// <summary>Gets whether the window is idle and can start work.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Gets whether a load or save round trip is running.</summary>
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
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanRemoveLine));
            }
        }
    }

    /// <summary>Gets whether the pending edits can be saved right now.</summary>
    public bool CanSave => HasSession && IsIdle && (Session?.HasChanges == true);

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

    /// <summary>Gets or sets the formatted semantic conflict list.</summary>
    public string ConflictsText
    {
        get => _conflictsText;
        private set
        {
            if (_conflictsText != value)
            {
                _conflictsText = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnSessionChanged()
    {
        OnPropertyChanged(nameof(CanSave));
    }

    private void AdoptOrder(OrderDto order, string status)
    {
        var draft = order.DeepClone();
        Session = draft.CreateEditSession(OnSessionChanged);
        SelectedLineIndex = -1;
        ConflictsText = string.Empty;
        Status = status;
    }

    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        if (!int.TryParse(OrderIdText, out var orderId))
        {
            Status = "The order id must be a number.";
            return;
        }

        IsBusy = true;
        Status = $"Loading order {orderId}.";
        try
        {
            var order = await _client.GetFromJsonAsync<OrderDto>($"orders/{orderId}");
            if (order is null)
            {
                Status = $"Order {orderId} returned no content.";
                return;
            }

            AdoptOrder(order, $"Loaded order {orderId} with {order.Lines.Count} lines.");
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

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var session = Session;
        if (session is null || IsBusy)
        {
            return;
        }

        var submitted = session.CreateChangeSet();
        if (submitted.IsEmpty)
        {
            Status = "No changes to save.";
            return;
        }

        IsBusy = true;
        Status = "Saving pending edits.";
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                $"orders/{OrderIdText.Trim()}"
            );
            // The token records the version observed at read time and guards
            // the write against a concurrent writer, even for disjoint edits.
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                session.Observable.Version.ToString()
            );
            request.Content = JsonContent.Create(submitted.ToPayload());
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                Status =
                    "The order changed since it was read. Load it again, re-apply the edits, and save with the fresh version.";
                return;
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                ConflictsText = await FormatConflictsAsync(response);
                Status =
                    "The submitted edits overlap current values. Resolve the conflicts above, then save again.";
                return;
            }

            response.EnsureSuccessStatusCode();
            var authoritative =
                await response.Content.ReadFromJsonAsync<OrderDto>()
                ?? throw new InvalidOperationException("The server returned no order.");
            if (!session.TryReconcile(submitted, authoritative, out var reconcileError))
            {
                Status =
                    $"The save succeeded but the session could not be reconciled: {reconcileError} Reload the order.";
                return;
            }

            // Start the next edit round from the authoritative response so
            // server-computed members and fresh keys are visible.
            AdoptOrder(authoritative, $"Saved. Order now has {authoritative.Lines.Count} lines.");
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
                UnitPrice = 10m,
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

    private static async Task<string> FormatConflictsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            var lines = new List<string>();
            foreach (var conflict in document.RootElement.EnumerateArray())
            {
                var path = conflict.TryGetProperty("path", out var pathValue)
                    ? pathValue.GetString()
                    : "?";
                var reason = conflict.TryGetProperty("reason", out var reasonValue)
                    ? reasonValue.GetString()
                    : conflict.ToString();
                lines.Add($"{path}: {reason}");
            }

            return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
