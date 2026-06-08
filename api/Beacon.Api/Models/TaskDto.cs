using System.ComponentModel.DataAnnotations;

namespace Beacon.Api.Models;

public record GoogleTaskListDto(string Id, string Title);

public record GoogleTaskDto(
    string Id,
    string TaskListId,
    string Title,
    string? Notes,
    string? Due,
    bool Completed,
    string? CompletedAt);

public record CreateTaskRequest(
    [Required][MinLength(1)] string Title,
    string? Notes,
    string? Due,
    string TaskListId);

public record UpdateTaskRequest(
    [Required][MinLength(1)] string Title,
    string? Notes,
    string? Due,
    bool Completed,
    string TaskListId);
