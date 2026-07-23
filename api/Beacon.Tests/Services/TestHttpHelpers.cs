namespace Beacon.Tests.Services;

internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Unexpected HTTP call in this test.");
}

internal sealed class FakeHttpMessageHandler(System.Net.HttpStatusCode status, string body)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

internal sealed class RecordingHttpMessageHandler(System.Net.HttpStatusCode status, string body)
    : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

internal sealed class SequentialHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public SequentialHttpMessageHandler(params (System.Net.HttpStatusCode status, string body)[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(
            responses.Select(r => new HttpResponseMessage(r.status)
            {
                Content = new StringContent(r.body, System.Text.Encoding.UTF8, "application/json"),
            }));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No more HTTP responses queued for {request.Method} {request.RequestUri}.");
        return Task.FromResult(_responses.Dequeue());
    }
}
