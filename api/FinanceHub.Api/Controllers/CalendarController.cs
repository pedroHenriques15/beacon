using FinanceHub.Api.Models;
using FinanceHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceHub.Api.Controllers;

[ApiController]
[Route("api/calendar")]
public class CalendarController(GoogleCalendarService calendarService) : ControllerBase
{
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents([FromQuery] DateTime start, [FromQuery] DateTime end, CancellationToken ct)
    {
        if (start >= end)
            return BadRequest("start must be before end.");
        try
        {
            var events = await calendarService.GetEventsAsync(start, end, ct);
            return Ok(events);
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

    [HttpPost("events")]
    public async Task<IActionResult> CreateEvent([FromBody] CreateCalendarEventRequest request, CancellationToken ct)
    {
        if (!request.IsAllDay && string.IsNullOrEmpty(request.End))
            return BadRequest("End is required for timed events.");
        try
        {
            var created = await calendarService.CreateEventAsync(request, ct);
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

    [HttpPut("events/{id}")]
    public async Task<IActionResult> UpdateEvent(string id, [FromBody] UpdateCalendarEventRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Event id is required.");
        if (!request.IsAllDay && string.IsNullOrEmpty(request.End))
            return BadRequest("End is required for timed events.");
        try
        {
            var calendarId = string.IsNullOrWhiteSpace(request.CalendarId) ? "primary" : request.CalendarId;
            var updated = await calendarService.UpdateEventAsync(id, calendarId, request, ct);
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

    [HttpDelete("events/{id}")]
    public async Task<IActionResult> DeleteEvent(string id, [FromQuery] string? calendarId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Event id is required.");
        try
        {
            var resolvedCalendarId = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;
            await calendarService.DeleteEventAsync(id, resolvedCalendarId, ct);
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
