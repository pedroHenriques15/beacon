using Beacon.Api.Features.Backup.Commands.CreateBackup;
using Beacon.Api.Features.Backup.Commands.RestoreBackup;
using Beacon.Api.Features.Backup.Queries.DownloadBackup;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackupController(
    CreateBackupCommandHandler createBackup,
    RestoreBackupCommandHandler restoreBackup,
    DownloadBackupQueryHandler downloadBackup) : ControllerBase
{
    [HttpGet("download")]
    public IActionResult DownloadBackup()
    {
        var result = downloadBackup.Handle();
        if (result is null)
            return NotFound(new { message = "No backup file found. Create a backup first." });

        return File(result.Stream, "application/json", result.FileName);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBackup(CancellationToken ct)
    {
        var result = await createBackup.HandleAsync(ct);
        return Ok(new { message = result.Message, path = result.Path });
    }

    [HttpPost("restore")]
    public async Task<IActionResult> RestoreBackup(CancellationToken ct)
    {
        var path = await restoreBackup.HandleAsync(ct);
        if (path is null)
            return NotFound(new { message = "No backup file found. Create a backup first." });

        return Ok(new { message = "Database restored successfully.", path });
    }
}
