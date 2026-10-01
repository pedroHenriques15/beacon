using System.ComponentModel.DataAnnotations;

namespace Beacon.Api.Models;

public record GoogleCalendarEventDto(
    string Id,
    string Title,
    string Start,
    string End,
    string? Description,
    string? Location,
    bool IsAllDay,
    string? ColorId,
    string CalendarId,
    string? CalendarColor,
    string? CalendarName);

public record CreateCalendarEventRequest(
    [Required][MinLength(1)] string Title,
    [Required] string Start,
    string? End,
    string? Description,
    string? Location,
    bool IsAllDay,
    string? ColorId);

public record UpdateCalendarEventRequest(
    [Required][MinLength(1)] string Title,
    [Required] string Start,
    string? End,
    string? Description,
    string? Location,
    bool IsAllDay,
    string? ColorId,
    string? CalendarId);
