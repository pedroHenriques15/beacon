namespace Beacon.Api.Features.Logs.Commands.LogClientError;

/// <summary>An uncaught error in the web client: its message, stack and the route it happened on.</summary>
public record LogClientErrorCommand(string? Message, string? Stack, string? Route);
