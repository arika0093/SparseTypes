using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using EfCoreSample.Contracts;
using SparseTypes;

namespace EfCoreSample.Client;

/// <summary>
/// Lets the user resolve semantic conflicts path by path, choosing the
/// submitted value or the current server value for each overlapping edit.
/// The caller applies the built <see cref="OrderDto.ChangeSet"/>.
/// </summary>
public partial class ConflictDialog : Window, INotifyPropertyChanged
{
    private readonly OrderDto.ChangeSet.Resolution _resolution;
    private string _errorText = string.Empty;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Creates a conflict dialog.</summary>
    /// <param name="summary">Explains why the dialog opened.</param>
    /// <param name="conflicts">The overlapping edits to resolve.</param>
    /// <param name="resolution">Collects the per-path decisions.</param>
    public ConflictDialog(
        string summary,
        IReadOnlyList<SparseConflict> conflicts,
        OrderDto.ChangeSet.Resolution resolution
    )
    {
        _resolution = resolution;
        Summary = summary;
        Rows = new ObservableCollection<ConflictRow>(
            conflicts.Select(static conflict => new ConflictRow(conflict))
        );
        foreach (var row in Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Gets the dialog explanation.</summary>
    public string Summary { get; }

    /// <summary>Gets the editable conflict rows.</summary>
    public ObservableCollection<ConflictRow> Rows { get; }

    /// <summary>Gets the resolution failure message, if Apply failed.</summary>
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText != value)
            {
                _errorText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Gets whether a resolution failure is displayed.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether every conflict has a decision.</summary>
    public bool CanApply => Rows.Count > 0 && Rows.All(static row => row.UseMine.HasValue);

    /// <summary>Gets the built change set after a successful Apply.</summary>
    public OrderDto.ChangeSet? Resolved { get; private set; }

    /// <summary>A single conflict row with the user's pending decision.</summary>
    public sealed class ConflictRow : INotifyPropertyChanged
    {
        private bool? _useMine;

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Creates a conflict row.</summary>
        /// <param name="conflict">The overlapping edit.</param>
        public ConflictRow(SparseConflict conflict)
        {
            Path = conflict.Path;
            PathText = conflict.PathText;
            KindText = conflict.Kind.ToString();
            MineText = FormatValue(conflict.LocalValue);
            ServerText = FormatValue(conflict.CurrentValue);
        }

        /// <summary>Gets the typed path used for the resolution decision.</summary>
        public SparsePath Path { get; }

        /// <summary>Gets the display path.</summary>
        public string PathText { get; }

        /// <summary>Gets the conflict kind.</summary>
        public string KindText { get; }

        /// <summary>Gets the submitted value text.</summary>
        public string MineText { get; }

        /// <summary>Gets the current server value text.</summary>
        public string ServerText { get; }

        /// <summary>Gets or sets the decision: true keeps mine, false takes the server value.</summary>
        public bool? UseMine
        {
            get => _useMine;
            set
            {
                if (_useMine != value)
                {
                    _useMine = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string FormatValue(Optional<object?> optional)
        {
            if (!optional.IsPresent)
            {
                return "(no value)";
            }

            return optional.Value switch
            {
                null => "(empty)",
                string text => text.Length == 0 ? "(empty)" : text,
                Guid guid => guid.ToString(),
                int number => number.ToString(CultureInfo.InvariantCulture),
                decimal number => number.ToString(CultureInfo.InvariantCulture),
                OrderLineDto line =>
                    $"Id={line.Id} Qty={line.Quantity} @ {line.UnitPrice.ToString(CultureInfo.InvariantCulture)}",
                var other => other.ToString() ?? "(empty)",
            };
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConflictRow.UseMine))
        {
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows)
        {
            if (row.UseMine is not { } useMine)
            {
                return;
            }

            if (useMine)
            {
                _resolution.UseIncoming(row.Path);
            }
            else
            {
                _resolution.UseCurrent(row.Path);
            }
        }

        if (!_resolution.TryBuild(out var resolved, out var failure) || resolved is null)
        {
            ErrorText = failure?.Reason ?? "The selected resolutions could not be built.";
            return;
        }

        Resolved = resolved;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
