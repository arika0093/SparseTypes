using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EfCoreSample.Contracts;
using SparseTypes;

namespace EfCoreSample.WinAppClient;

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
            System.Collections.IEnumerable items => FormatSequence(items),
            var other when TryFormatEndpoint(other, out var endpointText) => endpointText,
            var other => other.ToString() ?? "(empty)",
        };

    private static string FormatSequence(System.Collections.IEnumerable items)
    {
        var parts = new List<string>();
        foreach (var item in items)
        {
            parts.Add(FormatCore(item));
        }

        return $"[{string.Join(", ", parts)}]";
    }

    private static bool TryFormatEndpoint(object value, [NotNullWhen(true)] out string? text)
    {
        text = null;
        var type = value.GetType();
        if (
            !type.IsGenericType
            || type.GetGenericTypeDefinition() != typeof(SparseTypes.ChangePayloadEndpoint<>)
        )
        {
            return false;
        }

        var state = type.GetProperty("State")?.GetValue(value);
        text = state switch
        {
            SparseTypes.ChangePayloadState.Value => FormatCore(
                type.GetProperty("Value")?.GetValue(value)
            ),
            SparseTypes.ChangePayloadState.Null => "(empty)",
            SparseTypes.ChangePayloadState.Redacted => "(redacted)",
            _ => "(no value)",
        };
        return true;
    }
}
