using Switchyard.Models;

namespace Switchyard.Transport;

/// <summary>MQTT transport — pub/sub for IoT devices.</summary>
public sealed class MqttTransport : ITransport
{
    private readonly string _broker;
    private readonly string _topic;
    private readonly ILogger<MqttTransport> _logger;

    public string Name => "mqtt";

    public MqttTransport(string broker, string topic, ILogger<MqttTransport> logger)
    {
        _broker = broker;
        _topic = topic;
        _logger = logger;
    }

    public Task ListenAsync(MessageHandler handler, CancellationToken ct)
    {
        // TODO: Implement MQTT client connection, subscription, and message handling.
        _logger.LogInformation("MQTT transport listening: broker={Broker}, topic={Topic}", _broker, _topic);
        return Task.Delay(Timeout.Infinite, ct);
    }

    public Task SendAsync(MessageTarget target, byte[] payload, CancellationToken ct)
    {
        // TODO: Implement MQTT publish to target endpoint.
        _logger.LogDebug("MQTT send: {Target}, {Bytes} bytes", target.Endpoint, payload.Length);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
