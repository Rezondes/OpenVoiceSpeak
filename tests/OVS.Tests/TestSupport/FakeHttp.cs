using System.Net;
using System.Text.Json;

namespace OVS.Tests.TestSupport;

/// <summary>Answers every request with the given function and remembers what was asked.</summary>
sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    /// <summary>Package M18: the answer, changeable after the handler was handed out.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    public static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
}
