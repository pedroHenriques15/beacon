using Microsoft.AspNetCore.Http;

namespace Beacon.Api.Features.Statements.Commands.UploadStatement;

public record UploadStatementCommand(IFormFile File);
