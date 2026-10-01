namespace Beacon.Api.Services;

/// <summary>
/// There is no Google access token to use. <see cref="State"/> says why: one of
/// <see cref="GoogleConnectionState"/>'s NotConnected, ReconnectRequired or Unreachable.
/// </summary>
public class GoogleConnectionException(string state) : InvalidOperationException(MessageFor(state))
{
    public string State { get; } = state;

    private static string MessageFor(string state) => state switch
    {
        GoogleConnectionState.ReconnectRequired => "The Google connection has expired; connect the Google account again.",
        GoogleConnectionState.Unreachable => "Google could not be reached to refresh the access token.",
        _ => "Google account is not connected.",
    };
}
