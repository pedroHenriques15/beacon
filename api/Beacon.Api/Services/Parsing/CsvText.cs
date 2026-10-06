using System.Text;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Reads an uploaded CSV file: its bytes as UTF-8 text, which the upload passes to the parsers as
/// the single page, and that text as rows of fields (RFC 4180: comma-separated, and a quoted field
/// may hold commas, line breaks and doubled quotes).
/// </summary>
public static class CsvText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsCsvFile(string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file's text, without a byte order mark. Refuses bytes that are not UTF-8.</summary>
    public static string Decode(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        try
        {
            return StrictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            throw new FormatException("The CSV file is not UTF-8 text.");
        }
    }

    /// <summary>Every non-blank line's fields, the header line included.</summary>
    public static List<string[]> ReadRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                    inQuotes = false;
            }
            else if (c == '"')
                inQuotes = true;
            else if (c == ',')
                EndField();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else
                field.Append(c);
        }

        if (inQuotes)
            throw new FormatException("The CSV file ends inside a quoted field.");
        EndRow();
        return rows;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (fields.Count > 1 || fields[0].Length > 0)
                rows.Add([.. fields]);
            fields.Clear();
        }
    }
}
