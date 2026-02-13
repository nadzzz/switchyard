// Switchyard C# — voice-first message dispatch daemon.
// Port of the Go switchyard daemon to ASP.NET Core.
// Interprets audio/text inputs and routes structured commands to target services.

using Microsoft.Extensions.Options;
using Switchyard.Config;
using Switchyard.DependencyInjection;
using Switchyard.Endpoints;
using Switchyard.Health;
using Switchyard.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration — Options pattern with validation.
// ---------------------------------------------------------------------------
builder.Configuration.AddEnvironmentVariables("SWITCHYARD_");
MapEnvToConfig(builder.Configuration);

builder.Services
    .AddSwitchyardOptions(builder.Configuration)
    .AddSwitchyardInterpreter()
    .AddSwitchyardTts()
    .AddSwitchyardTransports()
    .AddSwitchyardDispatcher()
    .AddSwitchyardHealthChecks();

// OpenAPI (built-in — replaces Swashbuckle).
builder.Services.AddOpenApi();

// Global JSON serialization using source-generated context.
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.TypeInfoResolverChain.Insert(0, SwitchyardJsonContext.Default));

// Problem Details for consistent error responses.
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Apply log level from options after building.
builder.Services.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
    return new ConfigureOptions<LoggerFilterOptions>(filter =>
    {
        var logLevel = opts.Logging.Level.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information
        };
        filter.MinLevel = logLevel;
    });
});

// ---------------------------------------------------------------------------
// Build & configure the pipeline.
// ---------------------------------------------------------------------------
var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var cfg = app.Services.GetRequiredService<IOptions<SwitchyardOptions>>().Value;

app.UseExceptionHandler();
app.UseStatusCodePages();

// OpenAPI / Swagger UI.
app.MapOpenApi();

// Health endpoints (built-in health checks).
app.MapSwitchyardHealthChecks();

// Dispatch endpoints (grouped).
app.MapDispatchEndpoints();

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------
logger.LogInformation("Switchyard C# starting — interpreter={Backend}, http_port={HttpPort}, health_port={HealthPort}",
    cfg.Interpreter.Backend, cfg.Transports.Http.Port, cfg.Server.HealthPort);

// Mark ready via the health check.
var healthCheck = app.Services.GetRequiredService<SwitchyardHealthCheck>();
healthCheck.MarkReady();
logger.LogInformation("Switchyard C# ready");

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static void MapEnvToConfig(ConfigurationManager config)
{
    // Map SWITCHYARD_* single-underscore env vars to Switchyard:* hierarchical keys.
    // The Go daemon uses single-underscore separators; ASP.NET Core uses double-underscore.
    var mapping = new Dictionary<string, string>
    {
        ["SWITCHYARD_INTERPRETER_BACKEND"] = "Switchyard:Interpreter:Backend",
        ["SWITCHYARD_INTERPRETER_OPENAI_API_KEY"] = "Switchyard:Interpreter:OpenAI:ApiKey",
        ["SWITCHYARD_INTERPRETER_OPENAI_TRANSCRIPTION_MODEL"] = "Switchyard:Interpreter:OpenAI:TranscriptionModel",
        ["SWITCHYARD_INTERPRETER_OPENAI_COMPLETION_MODEL"] = "Switchyard:Interpreter:OpenAI:CompletionModel",
        ["SWITCHYARD_INTERPRETER_LOCAL_WHISPER_ENDPOINT"] = "Switchyard:Interpreter:Local:WhisperEndpoint",
        ["SWITCHYARD_INTERPRETER_LOCAL_WHISPER_TYPE"] = "Switchyard:Interpreter:Local:WhisperType",
        ["SWITCHYARD_INTERPRETER_LOCAL_LLM_ENDPOINT"] = "Switchyard:Interpreter:Local:LlmEndpoint",
        ["SWITCHYARD_INTERPRETER_LOCAL_LLM_MODEL"] = "Switchyard:Interpreter:Local:LlmModel",
        ["SWITCHYARD_INTERPRETER_LOCAL_VAD_FILTER"] = "Switchyard:Interpreter:Local:VadFilter",
        ["SWITCHYARD_INTERPRETER_LOCAL_LANGUAGE"] = "Switchyard:Interpreter:Local:Language",
        ["SWITCHYARD_TTS_ENABLED"] = "Switchyard:Tts:Enabled",
        ["SWITCHYARD_TTS_BACKEND"] = "Switchyard:Tts:Backend",
        ["SWITCHYARD_TTS_PIPER_ENDPOINT"] = "Switchyard:Tts:Piper:Endpoint",
        ["SWITCHYARD_TTS_PIPER_ENDPOINTS_EN"] = "Switchyard:Tts:Piper:Endpoints:en",
        ["SWITCHYARD_TTS_PIPER_ENDPOINTS_FR"] = "Switchyard:Tts:Piper:Endpoints:fr",
        ["SWITCHYARD_TTS_PIPER_ENDPOINTS_ES"] = "Switchyard:Tts:Piper:Endpoints:es",
        ["SWITCHYARD_TRANSPORTS_HTTP_ENABLED"] = "Switchyard:Transports:Http:Enabled",
        ["SWITCHYARD_TRANSPORTS_HTTP_PORT"] = "Switchyard:Transports:Http:Port",
        ["SWITCHYARD_TRANSPORTS_GRPC_ENABLED"] = "Switchyard:Transports:Grpc:Enabled",
        ["SWITCHYARD_TRANSPORTS_GRPC_PORT"] = "Switchyard:Transports:Grpc:Port",
        ["SWITCHYARD_TRANSPORTS_MQTT_ENABLED"] = "Switchyard:Transports:Mqtt:Enabled",
        ["SWITCHYARD_TRANSPORTS_MQTT_BROKER"] = "Switchyard:Transports:Mqtt:Broker",
        ["SWITCHYARD_TRANSPORTS_MQTT_TOPIC"] = "Switchyard:Transports:Mqtt:Topic",
        ["SWITCHYARD_SERVER_HEALTH_PORT"] = "Switchyard:Server:HealthPort",
        ["SWITCHYARD_LOGGING_LEVEL"] = "Switchyard:Logging:Level",
        ["SWITCHYARD_LOGGING_FORMAT"] = "Switchyard:Logging:Format",
    };

    var env = new Dictionary<string, string?>();
    foreach (var (envKey, configKey) in mapping)
    {
        var val = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(val))
            env[configKey] = val;
    }

    // Also resolve OPENAI_API_KEY as a fallback.
    var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrEmpty(openaiKey) && !env.ContainsKey("Switchyard:Interpreter:OpenAI:ApiKey"))
        env["Switchyard:Interpreter:OpenAI:ApiKey"] = openaiKey;

    if (env.Count > 0)
        config.AddInMemoryCollection(env);
}
