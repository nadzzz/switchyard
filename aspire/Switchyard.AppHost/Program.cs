// Switchyard Aspire AppHost — local development orchestration.
// This project uses .NET Aspire to launch the switchyard Go daemon alongside
// any dependent services (future: Redis, PostgreSQL, Mosquitto).
//
// Prerequisites:
//   - .NET 9 SDK
//   - Go 1.23+
//   - VS Code with C# Dev Kit and .NET Aspire extensions
//
// Usage:
//   dotnet run --project aspire/Switchyard.AppHost
//   (or press F5 in VS Code with the "Aspire AppHost" launch config)

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Switchyard Go daemon
// ---------------------------------------------------------------------------
var switchyard = builder.AddGolangApp("switchyard", workingDirectory: "../../")
    .WithHttpEndpoint(port: 8080, env: "SWITCHYARD_TRANSPORTS_HTTP_PORT", name: "http")
    .WithHttpEndpoint(port: 8081, env: "SWITCHYARD_SERVER_HEALTH_PORT", name: "health")
    .WithEnvironment("SWITCHYARD_TRANSPORTS_GRPC_PORT", "50051")
    .WithEnvironment("SWITCHYARD_INTERPRETER_BACKEND", "openai")
    .WithEnvironment("SWITCHYARD_LOGGING_LEVEL", "debug")
    .WithEnvironment("SWITCHYARD_LOGGING_FORMAT", "text");

// Inject secrets from user-secrets or environment.
// To set locally: dotnet user-secrets set "OPENAI_API_KEY" "sk-..."
if (builder.Configuration["OPENAI_API_KEY"] is string openaiKey && openaiKey.Length > 0)
{
    switchyard = switchyard.WithEnvironment("OPENAI_API_KEY", openaiKey);
}

if (builder.Configuration["HA_TOKEN"] is string haToken && haToken.Length > 0)
{
    switchyard = switchyard.WithEnvironment("HA_TOKEN", haToken);
}

// ---------------------------------------------------------------------------
// Future: add dependent services here
// ---------------------------------------------------------------------------
// var redis = builder.AddRedis("cache");
// switchyard.WithReference(redis);

// var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2")
//     .WithEndpoint(port: 1883, name: "mqtt");

builder.Build().Run();
