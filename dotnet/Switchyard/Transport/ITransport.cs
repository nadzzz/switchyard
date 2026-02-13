using Switchyard.Models;

namespace Switchyard.Transport;

/// <summary>Handler function that processes an incoming message and returns a result.</summary>
public delegate Task<DispatchResult> MessageHandler(DispatchMessage message, CancellationToken ct);

/// <summary>
/// Interface for pluggable message transports.
/// Each transport (gRPC, HTTP, MQTT) implements this interface.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    string Name { get; }

    /// <summary>Start accepting incoming messages, dispatching them to the handler.</summary>
    Task ListenAsync(MessageHandler handler, CancellationToken ct);

    /// <summary>Deliver a payload to a target address using this transport's protocol.</summary>
    Task SendAsync(MessageTarget target, byte[] payload, CancellationToken ct);
}
