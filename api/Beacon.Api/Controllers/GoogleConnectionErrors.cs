using Beacon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

/// <summary>
/// The answer of a Calendar or Tasks endpoint that had no Google access token to use: 401 when
/// the account must be connected (again) in Settings, 503 when Google could not be reached.
/// <c>code</c> tells the client which case it is.
/// </summary>
public static class GoogleConnectionErrors
{
    public const string NotConnected = "google_not_connected";
    public const string ReconnectRequired = "google_reconnect_required";
    public const string Unreachable = "google_unreachable";

    public static ObjectResult ToResult(GoogleConnectionException ex) => ex.State switch
    {
        GoogleConnectionState.ReconnectRequired => Result(401, ReconnectRequired, ex),
        GoogleConnectionState.Unreachable => Result(503, Unreachable, ex),
        _ => Result(401, NotConnected, ex),
    };

    private static ObjectResult Result(int status, string code, GoogleConnectionException ex) =>
        new(new { code, error = ex.Message }) { StatusCode = status };
}
