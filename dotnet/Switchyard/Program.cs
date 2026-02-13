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
// Environment variables use standard ASP.NET Core convention:
//   Switchyard__Interpreter__Backend=openai  →  Switchyard:Interpreter:Backend
// ---------------------------------------------------------------------------
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
