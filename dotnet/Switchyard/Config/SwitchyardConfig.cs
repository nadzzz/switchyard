// Switchyard configuration model — mirrors the Go config.Config struct.
// Bound from YAML/env vars via ASP.NET Core configuration.

namespace Switchyard.Config;

/// <summary>Root configuration for the switchyard daemon.</summary>
public sealed class SwitchyardConfig
{
    public const string SectionName = "Switchyard";

    public ServerConfig Server { get; set; } = new();
    public TransportsConfig Transports { get; set; } = new();
    public InterpreterConfig Interpreter { get; set; } = new();
    public TtsConfig Tts { get; set; } = new();
    public Dictionary<string, TargetConfig> Targets { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class ServerConfig
{
    public int HealthPort { get; set; } = 8081;
}

public sealed class TransportsConfig
{
    public GrpcTransportConfig Grpc { get; set; } = new();
    public HttpTransportConfig Http { get; set; } = new();
    public MqttTransportConfig Mqtt { get; set; } = new();
}

public sealed class GrpcTransportConfig
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 50051;
}

public sealed class HttpTransportConfig
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8080;
}

public sealed class MqttTransportConfig
{
    public bool Enabled { get; set; }
    public string Broker { get; set; } = "tcp://localhost:1883";
    public string Topic { get; set; } = "switchyard/#";
}

public sealed class InterpreterConfig
{
    public string Backend { get; set; } = "openai"; // "openai" | "local"
    public OpenAIConfig OpenAI { get; set; } = new();
    public LocalConfig Local { get; set; } = new();
}

public sealed class OpenAIConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string CompletionModel { get; set; } = "gpt-4o";
}

public sealed class LocalConfig
{
    public string WhisperEndpoint { get; set; } = "http://localhost:8000/v1/audio/transcriptions";
    public string WhisperType { get; set; } = "openai"; // "openai" | "asr"
    public string LlmEndpoint { get; set; } = "http://localhost:11434/api/generate";
    public string LlmModel { get; set; } = "llama3";
    public bool VadFilter { get; set; }
    public string Language { get; set; } = string.Empty;
}

public sealed class TargetConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public sealed class TtsConfig
{
    public bool Enabled { get; set; }
    public string Backend { get; set; } = "piper";
    public PiperConfig Piper { get; set; } = new();
}

public sealed class PiperConfig
{
    public string Endpoint { get; set; } = "localhost:10200";
    public Dictionary<string, string> Endpoints { get; set; } = new();
    public Dictionary<string, string> Voices { get; set; } = new();
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "info";
    public string Format { get; set; } = "json";
}
