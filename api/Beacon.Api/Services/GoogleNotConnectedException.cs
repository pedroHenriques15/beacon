namespace Beacon.Api.Services;

public class GoogleNotConnectedException()
    : InvalidOperationException("Google account is not connected.");
