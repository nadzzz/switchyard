// Switchyard C# — voice-first message dispatch daemon.
// Port of the Go switchyard daemon to ASP.NET Core.
// Interprets audio/text inputs and routes structured commands to target services.

using System.Text.Json;
using Switchyard.Config;
using Switchyard.Dispatch;
using Switchyard.Interpreter;
using Switchyard.Models;
using Switchyard.Transport;
using Switchyard.Tts;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration — bind from environment variables (SWITCHYARD_*) and appsettings.
// Environment variable mapping: SWITCHYARD_INTERPRETER_BACKEND → Switchyard:Interpreter:Backend
// ---------------------------------------------------------------------------
builder.Configuration.AddEnvironmentVariables("SWITCHYARD_");

// Map flat SWITCHYARD_ env vars to the hierarchical config model.
// The standard ASP.NET approach uses __ (double underscore) as separator,
// but the Go daemon uses _ (single underscore). We map them manually.
MapEnvToConfig(builder.Configuration);

var cfg = new SwitchyardConfig();
builder.Configuration.GetSection("Switchyard").Bind(cfg);

// Apply defaults for anything not set.
if (cfg.Transports.Http.Port == 0) cfg.Transports.Http.Port = 8080;
if (cfg.Transports.Grpc.Port == 0) cfg.Transports.Grpc.Port = 50051;
if (cfg.Server.HealthPort == 0) cfg.Server.HealthPort = 8081;

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var logLevel = cfg.Logging.Level.ToLowerInvariant() switch
{
    "debug" => LogLevel.Debug,
    "warn" or "warning" => LogLevel.Warning,
    "error" => LogLevel.Error,
    _ => LogLevel.Information
};
builder.Logging.SetMinimumLevel(logLevel);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(cfg);
builder.Services.AddHttpClient();

// Interpreter
builder.Services.AddSingleton<IInterpreter>(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    return cfg.Interpreter.Backend.ToLowerInvariant() switch
    {
        "local" => new LocalInterpreter(cfg.Interpreter.Local,
            httpFactory.CreateClient(), sp.GetRequiredService<ILogger<LocalInterpreter>>()),
        _ => new OpenAIInterpreter(cfg.Interpreter.OpenAI,
            httpFactory.CreateClient(), sp.GetRequiredService<ILogger<OpenAIInterpreter>>())
    };
});

// TTS
if (cfg.Tts.Enabled)
{
    builder.Services.AddSingleton<ISynthesizer>(sp =>
        cfg.Tts.Backend.ToLowerInvariant() switch
        {
            "piper" => new PiperSynthesizer(cfg.Tts.Piper, sp.GetRequiredService<ILogger<PiperSynthesizer>>()),
            _ => throw new InvalidOperationException($"Unknown TTS backend: {cfg.Tts.Backend}")
        });
}

// Dispatcher
builder.Services.AddSingleton<Dispatcher>(sp =>
{
    var interp = sp.GetRequiredService<IInterpreter>();
    var synth = sp.GetService<ISynthesizer>();
    var logger = sp.GetRequiredService<ILogger<Dispatcher>>();

    var transports = new List<ITransport>();
    if (cfg.Transports.Grpc.Enabled)
        transports.Add(new GrpcTransport(cfg.Transports.Grpc.Port,
            sp.GetRequiredService<ILogger<GrpcTransport>>()));
    if (cfg.Transports.Mqtt.Enabled)
        transports.Add(new MqttTransport(cfg.Transports.Mqtt.Broker, cfg.Transports.Mqtt.Topic,
            sp.GetRequiredService<ILogger<MqttTransport>>()));

    return new Dispatcher(interp, transports, synth, logger);
});

// ---------------------------------------------------------------------------
// Build the app
// ---------------------------------------------------------------------------
var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.UseSwagger();
app.UseSwaggerUI();

// ---------------------------------------------------------------------------
// Health endpoints
// ---------------------------------------------------------------------------
var ready = false;

app.MapGet("/healthz", () => ready
    ? Results.Ok(new { status = "ok" })
    : Results.StatusCode(503))
    .WithName("Healthz")
    .WithOpenApi();

app.MapGet("/readyz", () => ready
    ? Results.Ok(new { status = "ok" })
    : Results.StatusCode(503))
    .WithName("Readyz")
    .WithOpenApi();

// ---------------------------------------------------------------------------
// Dispatch endpoint
// ---------------------------------------------------------------------------
var dispatcher = app.Services.GetRequiredService<Dispatcher>();

app.MapPost("/dispatch", async (HttpContext ctx) =>
{
    var msg = new DispatchMessage();
    var contentType = ctx.Request.ContentType ?? "";

    if (contentType.Contains("application/json"))
    {
        msg = await ctx.Request.ReadFromJsonAsync<DispatchMessage>(ctx.RequestAborted) ?? new();
    }
    else
    {
        // Raw audio
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        msg.Audio = ms.ToArray();
        msg.ContentType = contentType;
        msg.Source = ctx.Request.Headers["X-Switchyard-Source"].FirstOrDefault() ?? "";

        var instrHeader = ctx.Request.Headers["X-Switchyard-Instruction"].FirstOrDefault();
        if (!string.IsNullOrEmpty(instrHeader))
            msg.Instruction = JsonSerializer.Deserialize<Instruction>(instrHeader) ?? new();
    }

    var result = await dispatcher.HandleAsync(msg, ctx.RequestAborted);
    return Results.Ok(result);
})
.WithName("Dispatch")
.Accepts<DispatchMessage>("application/json")
.Produces<DispatchResult>()
.WithOpenApi();

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------
logger.LogInformation("Switchyard C# starting — interpreter={Backend}, http_port={HttpPort}, health_port={HealthPort}",
    cfg.Interpreter.Backend, cfg.Transports.Http.Port, cfg.Server.HealthPort);

ready = true;
logger.LogInformation("Switchyard C# ready");

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static void MapEnvToConfig(ConfigurationManager config)
{
    // Map SWITCHYARD_* single-underscore env vars to Switchyard:* hierarchical keys.
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

    // Also resolve OPENAI_API_KEY as a fallback
    var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrEmpty(openaiKey) && !env.ContainsKey("Switchyard:Interpreter:OpenAI:ApiKey"))
        env["Switchyard:Interpreter:OpenAI:ApiKey"] = openaiKey;

    if (env.Count > 0)
        config.AddInMemoryCollection(env);
}
