using System.Text.Json;
using Switchyard.Models;

namespace Switchyard.Transport;

/// <summary>HTTP transport — exposes POST /dispatch and Swagger UI.</summary>
public sealed class HttpTransport : ITransport
{
    private readonly int _port;
    private readonly ILogger<HttpTransport> _logger;
    private WebApplication? _app;

    public string Name => "http";

    public HttpTransport(int port, ILogger<HttpTransport> logger)
    {
        _port = port;
        _logger = logger;
    }

    public async Task ListenAsync(MessageHandler handler, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(_port));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Logging.ClearProviders(); // Use parent's logging

        _app = builder.Build();
        _app.UseSwagger();
        _app.UseSwaggerUI();

        _app.MapPost("/dispatch", async (HttpContext ctx) =>
        {
            var msg = new DispatchMessage();
            var contentType = ctx.Request.ContentType ?? "";

            if (contentType.Contains("application/json"))
            {
                msg = await ctx.Request.ReadFromJsonAsync<DispatchMessage>(ct) ?? new();
            }
            else
            {
                // Treat body as raw audio
                using var ms = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(ms, ct);
                msg.Audio = ms.ToArray();
                msg.ContentType = contentType;
                msg.Source = ctx.Request.Headers["X-Switchyard-Source"].FirstOrDefault() ?? "";

                var instrHeader = ctx.Request.Headers["X-Switchyard-Instruction"].FirstOrDefault();
                if (!string.IsNullOrEmpty(instrHeader))
                    msg.Instruction = JsonSerializer.Deserialize<Instruction>(instrHeader) ?? new();
            }

            var result = await handler(msg, ctx.RequestAborted);
            return Results.Ok(result);
        })
        .WithName("Dispatch")
        .WithOpenApi();

        _logger.LogInformation("HTTP transport listening on port {Port}", _port);

        await _app.RunAsync(ct);
    }

    public async Task SendAsync(MessageTarget target, byte[] payload, CancellationToken ct)
    {
        using var client = new HttpClient();
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var resp = await client.PostAsync(target.Endpoint, content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("HTTP send failed: {Status} {Body}", resp.StatusCode, body);
        }
        else
        {
            _logger.LogDebug("HTTP send success: {Target} {Status}", target.Endpoint, resp.StatusCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
