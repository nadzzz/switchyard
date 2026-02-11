// Switchyard Aspire AppHost — local development orchestration.
// This project uses .NET Aspire to launch the switchyard Go daemon alongside
// dependent services whose set varies by tier:
//
//   --localmachine   All Aspire-managed containers (whisper.cpp + Ollama)
//   --localnetwork   Use existing LAN services (nadznas Whisper, no Ollama yet)
//   (default)        Cloud / OpenAI
//
// Prerequisites:
//   - .NET 10 SDK + Aspire workload
//   - Docker Desktop (switchyard runs as a container)
//   - VS Code with C# Dev Kit and .NET Aspire extensions
//
// Usage:
//   dotnet run --project aspire/Switchyard.AppHost
//   dotnet run --project aspire/Switchyard.AppHost -- --localmachine
//   dotnet run --project aspire/Switchyard.AppHost -- --localnetwork

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Determine which tier we're running.
// CLI:    dotnet run -- --localmachine
// Env:    SWITCHYARD_TIER=localmachine
// Config: appsettings.json  { "Tier": "localmachine" }
// ---------------------------------------------------------------------------
var tier = builder.Configuration["Tier"]
        ?? (args.Contains("--localmachine") ? "localmachine"
          : args.Contains("--localnetwork") ? "localnetwork"
          : "cloud");

bool useLocalMachine = tier.Equals("localmachine", StringComparison.OrdinalIgnoreCase);
bool useLocalNetwork = tier.Equals("localnetwork", StringComparison.OrdinalIgnoreCase);

// ---------------------------------------------------------------------------
// Aspire-managed containers (localmachine tier only)
// ---------------------------------------------------------------------------
IResourceBuilder<OllamaModelResource>? ollamaModel = null;
IResourceBuilder<ContainerResource>? whisperContainer = null;

if (useLocalMachine)
{
    // Ollama — local LLM (Docker container, persisted volume)
    var ollama = builder.AddOllama("ollama")
        .WithDataVolume();

    ollamaModel = ollama.AddModel("llama3.2:1b");

    // whisper.cpp server — quantised GGML model (custom Dockerfile)
    whisperContainer = builder.AddDockerfile("whisper", "../../build/whisper-server")
        .WithHttpEndpoint(targetPort: 8080, name: "whisper");
}

// ---------------------------------------------------------------------------
// Switchyard Go daemon
// ---------------------------------------------------------------------------
var switchyard = builder.AddDockerfile("switchyard", "../../", "build/Dockerfile")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithHttpEndpoint(port: 8081, targetPort: 8081, name: "health")
    .WithEndpoint(port: 50051, targetPort: 50051, name: "grpc", scheme: "http")
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayText = url.Endpoint?.EndpointName switch
            {
                "http"   => "HTTP API",
                "health" => "Health",
                "grpc"   => "gRPC",
                _        => url.DisplayText
            };
        }

        var httpUrl = context.Urls.FirstOrDefault(u => u.Endpoint?.EndpointName == "http");
        if (httpUrl is not null)
        {
            context.Urls.Add(new ResourceUrlAnnotation
            {
                Url = $"{httpUrl.Url}/swagger/index.html",
                DisplayText = "Swagger UI"
            });
        }
    })
    .WithEnvironment("SWITCHYARD_LOGGING_LEVEL", "debug")
    .WithEnvironment("SWITCHYARD_LOGGING_FORMAT", "text");

if (useLocalMachine)
{
    // ── localmachine: whisper.cpp container + Ollama container ───────────
    switchyard = switchyard
        .WaitFor(ollamaModel!)
        .WaitFor(whisperContainer!)
        .WithEnvironment("SWITCHYARD_INTERPRETER_BACKEND", "local")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_WHISPER_TYPE", "openai")
        //.WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_LANGUAGE", "en")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_LLM_MODEL", "llama3.2:1b")
        // Whisper endpoint — resolved from the Aspire-managed container
        .WithEnvironment(ctx =>
        {
            var wEp = whisperContainer!.GetEndpoint("whisper");
            ctx.EnvironmentVariables["SWITCHYARD_INTERPRETER_LOCAL_WHISPER_ENDPOINT"] =
                ReferenceExpression.Create($"{wEp}/inference");
        })
        // Ollama endpoint — resolved from the Aspire-managed container
        .WithEnvironment(ctx =>
        {
            var oEp = ollamaModel!.Resource.Parent.PrimaryEndpoint;
            ctx.EnvironmentVariables["SWITCHYARD_INTERPRETER_LOCAL_LLM_ENDPOINT"] =
                ReferenceExpression.Create($"{oEp}/api/generate");
        });
}
else if (useLocalNetwork)
{
    // ── localnetwork: nadznas Whisper ASR, no LLM yet ───────────────────
    switchyard = switchyard
        .WithEnvironment("SWITCHYARD_INTERPRETER_BACKEND", "local")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_WHISPER_ENDPOINT", "http://nadznas:9300/asr")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_WHISPER_TYPE", "asr")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_VAD_FILTER", "true")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_LLM_ENDPOINT", "http://nadznas:11434/api/generate");
}
else
{
    // ── cloud: OpenAI (default) ─────────────────────────────────────────
    switchyard = switchyard.WithEnvironment("SWITCHYARD_INTERPRETER_BACKEND", "openai");
}

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
