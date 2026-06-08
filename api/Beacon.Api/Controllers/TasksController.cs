using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController(GoogleTasksService tasksService) : ControllerBase
{
    [HttpGet("lists")]
    public async Task<IActionResult> GetTaskLists(CancellationToken ct)
    {
        try
        {
            var lists = await tasksService.GetTaskListsAsync(ct);
            return Ok(lists);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks([FromQuery] string listId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(listId))
            return BadRequest("listId is required.");
        try
        {
            var tasks = await tasksService.GetTasksAsync(listId, ct);
            return Ok(tasks);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        try
        {
            var created = await tasksService.CreateTaskAsync(request, ct);
            return StatusCode(201, created);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(string id, [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await tasksService.UpdateTaskAsync(id, request, ct);
            return Ok(updated);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/move")]
    public async Task<IActionResult> MoveTask(string id, [FromBody] MoveTaskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceListId) || string.IsNullOrWhiteSpace(request.TargetListId))
            return BadRequest("sourceListId and targetListId are required.");
        try
        {
            var moved = await tasksService.MoveTaskAsync(id, request, ct);
            return Ok(moved);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(string id, [FromQuery] string listId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Task id is required.");
        if (string.IsNullOrWhiteSpace(listId))
            return BadRequest("listId is required.");
        try
        {
            await tasksService.DeleteTaskAsync(id, listId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            return StatusCode(503, new { error = "Google account is not connected." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
