using FinanceHub.Api.Services;

namespace FinanceHub.Api.Features.Statements.Commands.UploadStatement;

public class UploadStatementCommandHandler(StatementUploadService uploadService, ILogger<UploadStatementCommandHandler> logger)
{
    public Task<UploadResult> HandleAsync(UploadStatementCommand command, CancellationToken ct = default)
    {
        logger.LogInformation("UploadStatement: file={FileName}", command.File.FileName);
        return uploadService.ImportAsync(command.File);
    }
}
