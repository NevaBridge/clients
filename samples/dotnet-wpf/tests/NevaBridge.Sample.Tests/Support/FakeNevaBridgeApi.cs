using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NevaBridge.Sample.Tests.Support;

public sealed record RecordedRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

public sealed record CannedResponse(int Status, string Body);

/// <summary>
/// A real HTTP server on a loopback port that answers with queued responses and records every
/// request, so client tests exercise the actual wire format instead of a mocked handler.
/// </summary>
public sealed class FakeNevaBridgeApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CannedResponse> _responses = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    private FakeNevaBridgeApi(WebApplication app)
    {
        _app = app;
    }

    public Uri BaseUrl { get; private set; } = new("http://127.0.0.1/");

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public static async Task<FakeNevaBridgeApi> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        FakeNevaBridgeApi fake = new(app);
        app.Run(fake.HandleAsync);
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?
            .Addresses.Single() ?? throw new InvalidOperationException("The fake API has no listening address.");
        fake.BaseUrl = new Uri(address + "/");
        return fake;
    }

    public void Respond(int status, string body)
    {
        _responses.Enqueue(new CannedResponse(status, body));
    }

    private async Task HandleAsync(HttpContext context)
    {
        using StreamReader reader = new(context.Request.Body);
        string body = await reader.ReadToEndAsync();
        Dictionary<string, string> headers = context.Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        _requests.Enqueue(new RecordedRequest(
            context.Request.Method,
            context.Request.Path + context.Request.QueryString,
            headers,
            body));

        if (!_responses.TryDequeue(out CannedResponse? response))
        {
            response = new CannedResponse(500, """{"error":"NoCannedResponse","message":"The test queued no response."}""");
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.Body);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
