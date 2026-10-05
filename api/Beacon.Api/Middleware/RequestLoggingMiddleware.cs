using System.Diagnostics;

namespace Beacon.Api.Middleware;

/// <summary>
/// One line per request: method, path, status and duration. Never the query string, which can
/// carry search terms. Server errors log at Error, everything else at Information.
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            logger.Log(
                status >= 500 ? LogLevel.Error : LogLevel.Information,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0} ms",
                context.Request.Method, context.Request.Path.Value, status,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
