using System.Globalization;
using EfCoreSample.Contracts;
using SparseTypes;

namespace EfCoreSample.Client;

/// <summary>Formats change values for the changes table and conflict rows.</summary>
internal static class ChangeValueText
{
    /// <summary>Formats an optional change value, spelling out missing values.</summary>
    /// <param name="optional">The value to format.</param>
    /// <returns>Display text for tables and dialogs.</returns>
    public static string Format(Optional<object?> optional) =>
        !optional.IsPresent ? "(no value)" : FormatCore(optional.Value);

    /// <summary>Formats a plain value.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>Display text for tables and dialogs.</returns>
    public static string Format(object? value) => FormatCore(value);

    private static string FormatCore(object? value) =>
        value switch
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
