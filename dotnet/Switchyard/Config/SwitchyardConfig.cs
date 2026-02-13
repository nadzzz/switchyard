// Switchyard configuration model — mirrors the Go config.Config struct.
// Bound via the Options pattern with validation support.

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Switchyard.Config;

/// <summary>Root configuration for the switchyard daemon.</summary>
public sealed class SwitchyardOptions
{
    public const string SectionName = "Switchyard";

    [ValidateObjectMembers]
    public ServerOptions Server { get; set; } = new();

    [ValidateObjectMembers]
    public TransportsOptions Transports { get; set; } = new();

    [ValidateObjectMembers]
    public InterpreterOptions Interpreter { get; set; } = new();

    [ValidateObjectMembers]
    public TtsOptions Tts { get; set; } = new();
    public Dictionary<string, TargetOptions> Targets { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public sealed class ServerOptions
{
    [Range(1, 65535)]
    public int HealthPort { get; set; } = 8081;
}

public sealed class TransportsOptions
{
    [ValidateObjectMembers]
    public GrpcTransportOptions Grpc { get; set; } = new();

    [ValidateObjectMembers]
    public HttpTransportOptions Http { get; set; } = new();

    [ValidateObjectMembers]
    public MqttTransportOptions Mqtt { get; set; } = new();
}

public sealed class GrpcTransportOptions
{
    public bool Enabled { get; set; } = true;

    [Range(1, 65535)]
    public int Port { get; set; } = 50051;
}

public sealed class HttpTransportOptions
{
    public bool Enabled { get; set; } = true;

    [Range(1, 65535)]
    public int Port { get; set; } = 8080;
}

public sealed class MqttTransportOptions
{
    public bool Enabled { get; set; }
    public string Broker { get; set; } = "tcp://localhost:1883";
    public string Topic { get; set; } = "switchyard/#";
}

public sealed class InterpreterOptions
{
    [Required, RegularExpression("^(openai|local)$", ErrorMessage = "Backend must be 'openai' or 'local'.")]
    public string Backend { get; set; } = "openai";

    [ValidateObjectMembers]
    public OpenAIOptions OpenAI { get; set; } = new();

    [ValidateObjectMembers]
    public LocalOptions Local { get; set; } = new();
}

public sealed class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";

    [Required]
    public string CompletionModel { get; set; } = "gpt-4o";
}

public sealed class LocalOptions
{
    [Required, Url]
    public string WhisperEndpoint { get; set; } = "http://localhost:8000/v1/audio/transcriptions";

    [Required, RegularExpression("^(openai|asr)$")]
    public string WhisperType { get; set; } = "openai";

    [Required, Url]
    public string LlmEndpoint { get; set; } = "http://localhost:11434/api/generate";

    [Required]
    public string LlmModel { get; set; } = "llama3";
    public bool VadFilter { get; set; }
    public string Language { get; set; } = string.Empty;
}

public sealed class TargetOptions
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string Protocol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public sealed class TtsOptions
{
    public bool Enabled { get; set; }

    [Required]
    public string Backend { get; set; } = "piper";

    [ValidateObjectMembers]
    public PiperOptions Piper { get; set; } = new();
}

public sealed class PiperOptions
{
    [Required]
    public string Endpoint { get; set; } = "localhost:10200";
    public Dictionary<string, string> Endpoints { get; set; } = new();
    public Dictionary<string, string> Voices { get; set; } = new();
}

public sealed class LoggingOptions
{
    public string Level { get; set; } = "info";
    public string Format { get; set; } = "json";
}

/// <summary>Validates <see cref="SwitchyardOptions"/> after binding.</summary>
[OptionsValidator]
public sealed partial class SwitchyardOptionsValidator : IValidateOptions<SwitchyardOptions>;

/// <summary>Resolves <c>${ENV_VAR}</c> references in the OpenAI API key.</summary>
public sealed class SwitchyardOptionsPostConfigure : IPostConfigureOptions<SwitchyardOptions>
{
    public void PostConfigure(string? name, SwitchyardOptions options)
    {
        // Resolve ${ENV_VAR} references in API key.
        options.Interpreter.OpenAI.ApiKey = ResolveEnvRef(options.Interpreter.OpenAI.ApiKey);

        // Fallback to OPENAI_API_KEY environment variable.
        if (string.IsNullOrEmpty(options.Interpreter.OpenAI.ApiKey))
        {
            var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
                options.Interpreter.OpenAI.ApiKey = envKey;
        }

        // Apply port defaults.
        if (options.Transports.Http.Port == 0) options.Transports.Http.Port = 8080;
        if (options.Transports.Grpc.Port == 0) options.Transports.Grpc.Port = 50051;
        if (options.Server.HealthPort == 0) options.Server.HealthPort = 8081;
    }

    private static string ResolveEnvRef(string val)
    {
        if (val.StartsWith("${") && val.EndsWith("}"))
        {
            var envKey = val[2..^1];
            return Environment.GetEnvironmentVariable(envKey) ?? val;
        }
        return val;
    }
}
