using System.Buffers;
using System.Globalization;
using System.Text.Json;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Concurrency;

internal static class ConcurrencyJson
{
    internal static string WriteDto(ConcurrentOrderDto dto)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Customer", dto.Customer);
            writer.WriteString("Notes", dto.Notes);
            writer.WriteEndObject();
        }

        return PlaygroundJson.DecodeUtf8(buffer);
    }

    internal static string WriteEntity(ConcurrentOrderEntity order)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("Id", order.Id);
            writer.WriteNumber("Revision", order.Revision);
            writer.WriteString("CustomerName", order.CustomerName);
            if (order.Note is null)
            {
                writer.WriteNull("Note");
            }
            else
            {
                writer.WriteStartObject("Note");
                writer.WriteNumber("Id", order.Note.Id);
                writer.WriteString("Text", order.Note.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return PlaygroundJson.DecodeUtf8(buffer);
    }

    internal static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
}
