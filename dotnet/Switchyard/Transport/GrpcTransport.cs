using Switchyard.Models;

namespace Switchyard.Transport;

/// <summary>gRPC transport — exposes a gRPC server for DispatchRequests.</summary>
public sealed class GrpcTransport : ITransport
{
    private readonly int _port;
    private readonly ILogger<GrpcTransport> _logger;

    public string Name => "grpc";

    public GrpcTransport(int port, ILogger<GrpcTransport> logger)
    {
        _port = port;
        _logger = logger;
    }

    public Task ListenAsync(MessageHandler handler, CancellationToken ct)
    {
        // TODO: Register generated SwitchyardService gRPC server once proto is compiled.
        _logger.LogInformation("gRPC transport listening on port {Port}", _port);
        return Task.Delay(Timeout.Infinite, ct);
    }

    public Task SendAsync(MessageTarget target, byte[] payload, CancellationToken ct)
    {
        // TODO: Implement gRPC client send to target endpoint.
        _logger.LogDebug("gRPC send: {Target}, {Bytes} bytes", target.Endpoint, payload.Length);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
