using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public class ContinenteParser : IGroceryReceiptParser
{
    public string ParserName => "Continente";

    public bool CanParse(string fullText) => fullText.Contains("Modelo Continente");

    public ParsedGroceryReceipt Parse(string fileName, IReadOnlyList<string> pages)
    {
        var text  = string.Join("\n", pages);
        var lines = text.Split('\n');

        var receiptDate = ExtractDate(text);
        var total       = ExtractTotal(text);
        var items       = ExtractItems(lines);

        return new ParsedGroceryReceipt("Continente", receiptDate, total, items);
    }

    private static DateOnly ExtractDate(string text)
    {
        var nroLine = text.Split('\n').FirstOrDefault(l => l.Contains("Nro:FS"));
        if (nroLine is not null)
        {
            var m = Regex.Match(nroLine, @"\b(\d{2}/\d{2}/\d{4})\b");
            if (m.Success)
                return DateOnly.ParseExact(m.Groups[1].Value, "dd/MM/yyyy", null);
        }
        return DateOnly.FromDateTime(DateTime.Today);
    }

    private static decimal ExtractTotal(string text)
    {
        var m = Regex.Match(text, @"TOTAL A PAGAR\s+([\d,]+)");
        if (m.Success)
            return ParsePt(m.Groups[1].Value);
        return 0m;
    }

    private static IReadOnlyList<ParsedGroceryItem> ExtractItems(string[] lines)
    {
        var startIdx = Array.FindIndex(lines, l => l.TrimEnd() == "IVA DESCRICAO VALOR");
        if (startIdx < 0) return [];

        var endIdx = -1;
        for (var i = startIdx + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("TOTAL A PAGAR", StringComparison.Ordinal))
            {
                endIdx = i;
                break;
            }
        }
        if (endIdx < 0) return [];

        var body   = lines[(startIdx + 1)..endIdx];
        var result = new List<ParsedGroceryItem>();

        string? currentCategory = null;
        var qtyOnlyPattern      = new Regex(@"^(\d+)\s+X\s+");
        var vatItemPattern      = new Regex(@"^\([A-Z]+\)\s+(.+)");
        var qtyLinePattern      = new Regex(@"^(\d+)\s+X\s+([\d,]+)\s+([\d,]+)$");
        var trailingPricePattern = new Regex(@"\s+([\d]+,[\d]+)$");

        var i2 = 0;
        while (i2 < body.Length)
        {
            var line = body[i2];

            if (string.IsNullOrWhiteSpace(line))
            {
                i2++;
                continue;
            }

            if (line.StartsWith("IVA Nao", StringComparison.Ordinal))
            {
                i2++;
                continue;
            }

            if (qtyOnlyPattern.IsMatch(line))
            {
                i2++;
                continue;
            }

            var vatMatch = vatItemPattern.Match(line);
            if (vatMatch.Success)
            {
                var descPart = vatMatch.Groups[1].Value;
                var priceMatch = trailingPricePattern.Match(descPart);

                if (priceMatch.Success)
                {
                    var description = descPart[..priceMatch.Index].Trim();
                    var amount      = ParsePt(priceMatch.Groups[1].Value);
                    result.Add(new ParsedGroceryItem(description, amount, 1, currentCategory));
                    i2++;
                }
                else if (i2 + 1 < body.Length)
                {
                    var nextLine = body[i2 + 1];
                    var qtyMatch = qtyLinePattern.Match(nextLine);
                    if (qtyMatch.Success)
                    {
                        var qty    = decimal.Parse(qtyMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        var amount = ParsePt(qtyMatch.Groups[3].Value);
                        result.Add(new ParsedGroceryItem(descPart.Trim(), amount, qty, currentCategory));
                        i2 += 2;
                    }
                    else
                    {
                        result.Add(new ParsedGroceryItem(descPart.Trim(), 0m, 1, currentCategory));
                        i2++;
                    }
                }
                else
                {
                    result.Add(new ParsedGroceryItem(descPart.Trim(), 0m, 1, currentCategory));
                    i2++;
                }
                continue;
            }

            if (line.StartsWith("NS ", StringComparison.Ordinal) && !Regex.IsMatch(line, @"^NS\s+[\d,]+"))
            {
                var description = line["NS ".Length..].Trim();

                var inlinePrice = trailingPricePattern.Match(description);
                if (inlinePrice.Success)
                {
                    var cleanDesc = description[..inlinePrice.Index].Trim();
                    var amount    = ParsePt(inlinePrice.Groups[1].Value);
                    result.Add(new ParsedGroceryItem(cleanDesc, amount, 1, currentCategory));
                    i2++;
                    continue;
                }

                if (i2 + 1 < body.Length)
                {
                    var nextLine = body[i2 + 1];
                    var qtyMatch = qtyLinePattern.Match(nextLine);
                    if (qtyMatch.Success)
                    {
                        var qty    = decimal.Parse(qtyMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        var amount = ParsePt(qtyMatch.Groups[3].Value);
                        result.Add(new ParsedGroceryItem(description, amount, qty, currentCategory));
                        i2 += 2;
                        continue;
                    }
                }
                result.Add(new ParsedGroceryItem(description, 0m, 1, currentCategory));
                i2++;
                continue;
            }

            if (line.TrimEnd().EndsWith(':') &&
                !line.StartsWith('(') &&
                !char.IsDigit(line[0]) &&
                !line.StartsWith("IVA", StringComparison.Ordinal))
            {
                currentCategory = line.TrimEnd(':').Trim();
                i2++;
                continue;
            }

            i2++;
        }

        return result;
    }

    private static decimal ParsePt(string s) =>
        decimal.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
}
