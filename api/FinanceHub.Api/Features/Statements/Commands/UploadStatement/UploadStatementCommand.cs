using Microsoft.AspNetCore.Http;

namespace FinanceHub.Api.Features.Statements.Commands.UploadStatement;

public record UploadStatementCommand(IFormFile File);
