using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EfCoreSample.Contracts;

namespace EfCoreSample.Client;

/// <summary>
/// Highlights DataGrid rows whose line has pending edits. Bound with a
/// multi binding of the row item and the window's change version so rows
/// refresh without rebuilding cell editors.
/// </summary>
public sealed class LineRowBrushConverter : IMultiValueConverter
{
    private static readonly Brush AddedBrush = CreateFrozen(0xE3, 0xF2, 0xE3);
    private static readonly Brush EditedBrush = CreateFrozen(0xFF, 0xF3, 0xC4);

    private readonly Func<OrderDto.EditSession?> _sessionProvider;

    /// <summary>Creates a row brush converter.</summary>
    /// <param name="sessionProvider">Returns the live edit session, if any.</param>
    public LineRowBrushConverter(Func<OrderDto.EditSession?> sessionProvider)
    {
        _sessionProvider = sessionProvider;
    }

    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [OrderLineDto.Observable row, ..])
        {
            return Brushes.Transparent;
        }

        var session = _sessionProvider();
        if (session is null)
        {
            return Brushes.Transparent;
        }

        var collection = session.CollectionState(OrderDto.SparsePath.Lines);
        var state =
            row.TemporaryId is { } temporary && temporary != Guid.Empty
                ? collection.GetTemporaryState(temporary)
                : collection.GetItemState(row.Id);
        if (state.IsAdded)
        {
            return AddedBrush;
        }

        return state.IsEdited || state.IsReordered || state.IsChanged
            ? EditedBrush
            : Brushes.Transparent;
    }

    /// <inheritdoc />
    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Row highlighting is one-way.");

    private static Brush CreateFrozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Binds a nullable row choice to Mine/Server radio buttons.</summary>
public sealed class ConflictChoiceConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool choice
        && parameter is string text
        && bool.TryParse(text, out var expected)
        && choice == expected;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string text && bool.TryParse(text, out var choice))
        {
            return choice;
        }

        return Binding.DoNothing;
    }
}

/// <summary>Shows the busy indicator while a round trip runs.</summary>
public sealed class BusyVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Busy display is one-way.");
}
