using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceHub.Api.Models;

namespace FinanceHub.Api.Services;

public class GoogleCalendarService(GoogleOAuthService oauthService, IHttpClientFactory httpClientFactory)
{
    private const string EventsBase = "https://www.googleapis.com/calendar/v3/calendars";
    private const string CalendarListUrl = "https://www.googleapis.com/calendar/v3/users/me/calendarList";

    public async Task<List<GoogleCalendarEventDto>> GetEventsAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var calendars = await GetCalendarListAsync(client, ct);

        var startStr = Uri.EscapeDataString(start.ToUniversalTime().ToString("o"));
        var endStr = Uri.EscapeDataString(end.ToUniversalTime().ToString("o"));

        var tasks = calendars.Select(cal => FetchEventsForCalendarAsync(client, cal, startStr, endStr, ct));
        var results = await Task.WhenAll(tasks);

        return [.. results.SelectMany(r => r)];
    }

    public async Task<GoogleCalendarEventDto> CreateEventAsync(CreateCalendarEventRequest request, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var body = BuildEventBody(request.Title, request.Start, request.End, request.Description, request.Location, request.IsAllDay, request.ColorId);
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{EventsBase}/primary/events", content, ct);
        await EnsureSuccessAsync(response, ct);

        var created = await response.Content.ReadFromJsonAsync<GoogleCalendarApiEvent>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty create response from Google Calendar.");

        return MapToDto(created, "primary", null, null);
    }

    public async Task<GoogleCalendarEventDto> UpdateEventAsync(string eventId, string calendarId, UpdateCalendarEventRequest request, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var body = BuildEventBody(request.Title, request.Start, request.End, request.Description, request.Location, request.IsAllDay, request.ColorId);
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var url = $"{EventsBase}/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        var response = await client.PutAsync(url, content, ct);
        await EnsureSuccessAsync(response, ct);

        var updated = await response.Content.ReadFromJsonAsync<GoogleCalendarApiEvent>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty update response from Google Calendar.");

        return MapToDto(updated, calendarId, null, null);
    }

    public async Task DeleteEventAsync(string eventId, string calendarId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var url = $"{EventsBase}/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        var response = await client.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<List<GoogleCalendarEntry>> GetCalendarListAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync($"{CalendarListUrl}?minAccessRole=reader", ct);
        await EnsureSuccessAsync(response, ct);

        var data = await response.Content.ReadFromJsonAsync<GoogleCalendarApiListResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty response from Google Calendar list.");

        return data.Items.Where(c => c.Selected).ToList();
    }

    private async Task<List<GoogleCalendarEventDto>> FetchEventsForCalendarAsync(
        HttpClient client, GoogleCalendarEntry calendar, string startStr, string endStr, CancellationToken ct)
    {
        var baseQuery = $"{EventsBase}/{Uri.EscapeDataString(calendar.Id)}/events"
            + $"?timeMin={startStr}&timeMax={endStr}&singleEvents=true&orderBy=startTime&maxResults=250";

        var all = new List<GoogleCalendarEventDto>();
        string? pageToken = null;

        do
        {
            var url = pageToken is null ? baseQuery : $"{baseQuery}&pageToken={Uri.EscapeDataString(pageToken)}";
            var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, ct);

            var data = await response.Content.ReadFromJsonAsync<GoogleCalendarApiEventsResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty response from Google Calendar.");

            all.AddRange(data.Items.Select(e => MapToDto(e, calendar.Id, calendar.BackgroundColor, calendar.Summary)));
            pageToken = data.NextPageToken;
        }
        while (pageToken is not null);

        return all;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google Calendar API error {(int)response.StatusCode}: {body}");
        }
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken ct)
    {
        var token = await oauthService.GetValidAccessTokenAsync(ct)
            ?? throw new InvalidOperationException("Google account is not connected.");

        var client = httpClientFactory.CreateClient("google-calendar");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Dictionary<string, object?> BuildEventBody(
        string title, string start, string? end,
        string? description, string? location, bool isAllDay, string? colorId)
    {
        var body = new Dictionary<string, object?> { ["summary"] = title };
        if (!string.IsNullOrEmpty(description)) body["description"] = description;
        if (!string.IsNullOrEmpty(location))    body["location"]    = location;
        if (!string.IsNullOrEmpty(colorId))     body["colorId"]     = colorId;

        if (isAllDay)
        {
            // Google Calendar all-day end is exclusive; add one day from the inclusive end the app uses.
            var exclusiveEnd = DateOnly.Parse(end ?? start).AddDays(1).ToString("yyyy-MM-dd");
            body["start"] = new { date = start };
            body["end"]   = new { date = exclusiveEnd };
        }
        else
        {
            body["start"] = new { dateTime = start };
            body["end"]   = new { dateTime = end ?? start };
        }

        return body;
    }

    private static GoogleCalendarEventDto MapToDto(GoogleCalendarApiEvent e, string calendarId, string? calendarColor, string? calendarName)
    {
        var isAllDay = e.Start?.Date is not null;
        var start = e.Start?.DateTime ?? e.Start?.Date
            ?? throw new InvalidOperationException($"Google Calendar event '{e.Id}' has no start date.");
        var rawEnd = e.End?.DateTime ?? e.End?.Date
            ?? throw new InvalidOperationException($"Google Calendar event '{e.Id}' has no end date.");
        // Google's all-day end is exclusive; subtract one day so the app always works with inclusive dates.
        var end = isAllDay ? DateOnly.Parse(rawEnd).AddDays(-1).ToString("yyyy-MM-dd") : rawEnd;
        return new GoogleCalendarEventDto(
            e.Id ?? throw new InvalidOperationException("Google Calendar API returned an event with no id."),
            e.Summary ?? "(No title)",
            start,
            end,
            e.Description,
            e.Location,
            isAllDay,
            e.ColorId,
            calendarId,
            calendarColor,
            calendarName);
    }

    private sealed class GoogleCalendarApiEventsResponse
    {
        [JsonPropertyName("items")]
        public List<GoogleCalendarApiEvent> Items { get; set; } = [];

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }
    }

    private sealed class GoogleCalendarApiListResponse
    {
        [JsonPropertyName("items")]
        public List<GoogleCalendarEntry> Items { get; set; } = [];
    }

    private sealed class GoogleCalendarEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("selected")]
        public bool Selected { get; set; }

        [JsonPropertyName("backgroundColor")]
        public string? BackgroundColor { get; set; }
    }

    private sealed class GoogleCalendarApiEvent
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("colorId")]
        public string? ColorId { get; set; }

        [JsonPropertyName("start")]
        public GoogleCalendarDateTime? Start { get; set; }

        [JsonPropertyName("end")]
        public GoogleCalendarDateTime? End { get; set; }
    }

    private sealed class GoogleCalendarDateTime
    {
        [JsonPropertyName("dateTime")]
        public string? DateTime { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }
    }
}
