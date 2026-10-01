using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beacon.Api.Features.Logs.Commands.LogClientError;
using Beacon.Api.Services.Logging;

namespace Beacon.Api.Features.Logs.Queries.GetLogs;

/// <summary>
/// Reads the log files newest first, each from its last line back, so the newest entries come
/// first and reading stops once the limit is reached or entries get older than the window.
/// </summary>
public partial class GetLogsQueryHandler(LogFiles logFiles)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    // Serilog's levels, lowest first; the compact format leaves "@l" out for Information.
    private static readonly string[] Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    public async Task<GetLogsResponse> HandleAsync(GetLogsQuery query, CancellationToken ct = default)
    {
        if (logFiles.Directory is null) return new GetLogsResponse(false, [], false);

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var minLevel = LevelRank(query.MinLevel) ?? LevelRank("Information")!.Value;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var entries = new List<LogEntryResponse>();
        foreach (var file in logFiles.NewestFirst())
        {
            // A file last written before the window holds nothing in it, nor does any older file.
            if (query.From is { } from && File.GetLastWriteTimeUtc(file) < from.UtcDateTime) break;

            var lines = await ReadLinesAsync(file, ct);
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (Parse(lines[i]) is not { } entry) continue;
                if (query.From is { } start && entry.Timestamp < start)
                    return new GetLogsResponse(true, entries, false);
                if (query.To is { } end && entry.Timestamp > end) continue;
                if (LevelRank(entry.Level) < minLevel) continue;
                if (search is not null && !Matches(entry, search)) continue;

                if (entries.Count == limit) return new GetLogsResponse(true, entries, true);
                entries.Add(entry);
            }
        }

        return new GetLogsResponse(true, entries, false);
    }

    private static int? LevelRank(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return null;
        var index = Array.FindIndex(Levels, l => string.Equals(l, level, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : index;
    }

    private static bool Matches(LogEntryResponse entry, string search) =>
        entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Details?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || entry.Source?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The whole file, shared with the logger still writing to it.</summary>
    private static async Task<List<string>> ReadLinesAsync(string path, CancellationToken ct)
    {
        var lines = new List<string>();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line) lines.Add(line);
        }
        catch (FileNotFoundException)
        {
            // Removed by the retention between listing and reading.
        }

        return lines;
    }

    /// <summary>One line of Serilog's compact JSON, or null for a line that isn't one (a half-written last line).</summary>
    internal static LogEntryResponse? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("@t", out var t) || !t.TryGetDateTimeOffset(out var timestamp))
                return null;

            return new LogEntryResponse(
                timestamp,
                Text(root, "@l") ?? "Information",
                Render(Text(root, "@mt") ?? Text(root, "@m") ?? "", root),
                Text(root, "SourceContext"),
                Text(root, "@x") ?? Text(root, LogClientErrorCommandHandler.StackProperty));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "{{" and "}}" escape braces; a property is {Name}, {@Name} or {$Name}, with an optional
    // alignment and format ({ElapsedMs:0}).
    [GeneratedRegex(@"\{\{|\}\}|\{[@$]?([A-Za-z0-9_]+)(?:,-?\d+)?(?::([^}]+))?\}")]
    private static partial Regex TemplateToken();

    /// <summary>The message template with its properties filled in, strings without quotes.</summary>
    internal static string Render(string template, JsonElement properties) =>
        TemplateToken().Replace(template, match =>
        {
            if (match.Value == "{{") return "{";
            if (match.Value == "}}") return "}";
            if (!properties.TryGetProperty(match.Groups[1].Value, out var value)) return match.Value;

            var format = match.Groups[2].Success ? match.Groups[2].Value : null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number when format is not null && value.TryGetDecimal(out var number) =>
                    number.ToString(format, CultureInfo.InvariantCulture),
                _ => value.GetRawText(),
            };
        });

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
