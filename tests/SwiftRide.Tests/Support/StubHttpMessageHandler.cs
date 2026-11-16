using System.Net;

namespace SwiftRide.Tests.Support;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return handler(request, cancellationToken);
    }

    public static HttpClient JsonClient(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Uri? baseAddress = null)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        }));

        return new HttpClient(handler)
        {
            BaseAddress = baseAddress ?? new Uri("http://localhost/")
        };
    }
}
