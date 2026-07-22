using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Api.Models;

namespace Beacon.Api.Services;

public class GoogleTasksService(GoogleOAuthService oauthService, IHttpClientFactory httpClientFactory)
{
    private const string BaseUrl = "https://tasks.googleapis.com/tasks/v1";

    public async Task<List<GoogleTaskListDto>> GetTaskListsAsync(CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var response = await client.GetAsync($"{BaseUrl}/users/@me/lists", ct);
        await EnsureSuccessAsync(response, ct);

        var data = await response.Content.ReadFromJsonAsync<TaskListsResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty response from Google Tasks list.");

        return data.Items.Select(l => new GoogleTaskListDto(l.Id, l.Title)).ToList();
    }

    public async Task<List<GoogleTaskDto>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var baseUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks?showCompleted=true&showHidden=true&maxResults=100";

        var all = new List<GoogleTaskDto>();
        string? pageToken = null;

        do
        {
            var url = pageToken is null ? baseUrl : $"{baseUrl}&pageToken={Uri.EscapeDataString(pageToken)}";
            var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, ct);

            var data = await response.Content.ReadFromJsonAsync<TasksResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty response from Google Tasks.");

            all.AddRange(data.Items.Select(item => MapToDto(item, listId)));
            pageToken = data.NextPageToken;
        }
        while (pageToken is not null);

        return all;
    }

    public async Task<GoogleTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var listId = string.IsNullOrWhiteSpace(request.TaskListId) ? "@default" : request.TaskListId;
        var body = BuildTaskBody(request.Title, request.Notes, request.Due, false);
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks", content, ct);
        await EnsureSuccessAsync(response, ct);

        var created = await response.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty create response from Google Tasks.");

        return MapToDto(created, listId);
    }

    public async Task<GoogleTaskDto> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var listId = string.IsNullOrWhiteSpace(request.TaskListId) ? "@default" : request.TaskListId;
        var body = BuildTaskBody(request.Title, request.Notes, request.Due, request.Completed);
        body["id"] = taskId;
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var url = $"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}";
        var response = await client.PutAsync(url, content, ct);
        await EnsureSuccessAsync(response, ct);

        var updated = await response.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty update response from Google Tasks.");

        return MapToDto(updated, listId);
    }

    public async Task DeleteTaskAsync(string taskId, string listId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);
        var url = $"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}";
        var response = await client.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<GoogleTaskDto> MoveTaskAsync(string taskId, MoveTaskRequest request, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(ct);

        if (request.SourceListId == request.TargetListId)
        {
            var moveUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(request.SourceListId)}/tasks/{Uri.EscapeDataString(taskId)}/move";
            if (!string.IsNullOrEmpty(request.PreviousTaskId))
                moveUrl += $"?previous={Uri.EscapeDataString(request.PreviousTaskId)}";

            var response = await client.PostAsync(moveUrl, new StringContent(""), ct);
            await EnsureSuccessAsync(response, ct);

            var moved = await response.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty move response from Google Tasks.");
            return MapToDto(moved, request.SourceListId);
        }
        else
        {
            // 1. Fetch task details from source list
            var getUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(request.SourceListId)}/tasks/{Uri.EscapeDataString(taskId)}";
            var getResponse = await client.GetAsync(getUrl, ct);
            await EnsureSuccessAsync(getResponse, ct);

            var sourceTask = await getResponse.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty task response from Google Tasks.");

            // 2. Create in target list
            var dueDate = string.IsNullOrEmpty(sourceTask.Due) ? null
                : (sourceTask.Due.Length >= 10 ? sourceTask.Due[..10] : sourceTask.Due);
            var body = BuildTaskBody(sourceTask.Title ?? "", sourceTask.Notes, dueDate, sourceTask.Status == "completed");
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var createUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(request.TargetListId)}/tasks";
            var createResponse = await client.PostAsync(createUrl, content, ct);
            await EnsureSuccessAsync(createResponse, ct);

            var newTask = await createResponse.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty create response from Google Tasks.");

            // 3. Position within target list if requested
            if (!string.IsNullOrEmpty(request.PreviousTaskId))
            {
                var newId = newTask.Id ?? throw new InvalidOperationException("Created task has no ID.");
                var posUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(request.TargetListId)}/tasks/{Uri.EscapeDataString(newId)}/move?previous={Uri.EscapeDataString(request.PreviousTaskId)}";
                var posResponse = await client.PostAsync(posUrl, new StringContent(""), ct);
                await EnsureSuccessAsync(posResponse, ct);
                newTask = await posResponse.Content.ReadFromJsonAsync<TaskItem>(cancellationToken: ct) ?? newTask;
            }

            // 4. Delete from source
            var deleteUrl = $"{BaseUrl}/lists/{Uri.EscapeDataString(request.SourceListId)}/tasks/{Uri.EscapeDataString(taskId)}";
            var deleteResponse = await client.DeleteAsync(deleteUrl, ct);
            await EnsureSuccessAsync(deleteResponse, ct);

            return MapToDto(newTask, request.TargetListId);
        }
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken ct)
    {
        var token = await oauthService.GetValidAccessTokenAsync(ct)
            ?? throw new GoogleNotConnectedException();

        var client = httpClientFactory.CreateClient("google-tasks");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Dictionary<string, object?> BuildTaskBody(string title, string? notes, string? due, bool completed)
    {
        var body = new Dictionary<string, object?> { ["title"] = title };
        body["status"] = completed ? "completed" : "needsAction";
        body["notes"] = notes ?? "";
        body["due"] = string.IsNullOrEmpty(due) ? null : $"{due}T00:00:00.000Z";
        return body;
    }

    private static GoogleTaskDto MapToDto(TaskItem item, string? overrideListId = null)
    {
        var completed = item.Status == "completed";
        string? dueDate = null;
        if (!string.IsNullOrEmpty(item.Due))
        {
            dueDate = item.Due.Length >= 10 ? item.Due[..10] : item.Due;
        }
        return new GoogleTaskDto(
            item.Id ?? throw new InvalidOperationException("Google Tasks API returned a task with no id."),
            overrideListId ?? item.TasklistId ?? "@default",
            item.Title ?? "(No title)",
            string.IsNullOrEmpty(item.Notes) ? null : item.Notes,
            dueDate,
            completed,
            item.Completed);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google Tasks API error {(int)response.StatusCode}: {body}");
        }
    }

    private sealed class TaskListsResponse
    {
        [JsonPropertyName("items")]
        public List<TaskListItem> Items { get; set; } = [];
    }

    private sealed class TaskListItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
    }

    private sealed class TasksResponse
    {
        [JsonPropertyName("items")]
        public List<TaskItem> Items { get; set; } = [];

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }
    }

    private sealed class TaskItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("due")]
        public string? Due { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("completed")]
        public string? Completed { get; set; }

        [JsonPropertyName("tasklistId")]
        public string? TasklistId { get; set; }
    }
}
