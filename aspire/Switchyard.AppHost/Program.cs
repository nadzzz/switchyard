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
IResourceBuilder<ContainerResource>? piperEnContainer = null;
IResourceBuilder<ContainerResource>? piperFrContainer = null;

if (useLocalMachine)
{
    // Ollama — local LLM (Docker container, persisted volume)
    var ollama = builder.AddOllama("ollama")
        .WithDataVolume();

    ollamaModel = ollama.AddModel("llama3.2:1b");

    // whisper.cpp server — quantised GGML model (custom Dockerfile)
    whisperContainer = builder.AddDockerfile("whisper", "../../build/whisper-server")
        .WithHttpEndpoint(targetPort: 8080, name: "whisper");

    // Piper TTS — per-language containers for fast, pre-loaded synthesis.
    piperEnContainer = builder.AddContainer("piper-en", "lscr.io/linuxserver/piper", "latest")
        .WithEndpoint(targetPort: 10200, name: "piper-en", scheme: "tcp")
        .WithVolume("piper-en-data", "/config")
        .WithEnvironment("PIPER_VOICE", "en_US-lessac-medium")
        .WithEnvironment("PUID", "1000")
        .WithEnvironment("PGID", "1000")
        .WithEnvironment("TZ", "Etc/UTC");

    piperFrContainer = builder.AddContainer("piper-fr", "lscr.io/linuxserver/piper", "latest")
        .WithEndpoint(targetPort: 10200, name: "piper-fr", scheme: "tcp")
        .WithVolume("piper-fr-data", "/config")
        .WithEnvironment("PIPER_VOICE", "fr_FR-siwis-medium")
        .WithEnvironment("PUID", "1000")
        .WithEnvironment("PGID", "1000")
        .WithEnvironment("TZ", "Etc/UTC");
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
    // ── localmachine: whisper.cpp container + Ollama container + Piper TTS (en + fr) ──
    switchyard = switchyard
        .WaitFor(ollamaModel!)
        .WaitFor(whisperContainer!)
        .WaitFor(piperEnContainer!)
        .WaitFor(piperFrContainer!)
        .WithEnvironment("SWITCHYARD_INTERPRETER_BACKEND", "local")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_WHISPER_TYPE", "openai")
        //.WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_LANGUAGE", "en")
        .WithEnvironment("SWITCHYARD_INTERPRETER_LOCAL_LLM_MODEL", "llama3.2:1b")
        .WithEnvironment("SWITCHYARD_TTS_ENABLED", "true")
        .WithEnvironment("SWITCHYARD_TTS_BACKEND", "piper")
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
        })
        // Piper TTS endpoints — per-language, resolved from Aspire-managed containers
        .WithEnvironment(ctx =>
        {
            var enEp = piperEnContainer!.GetEndpoint("piper-en");
            ctx.EnvironmentVariables["SWITCHYARD_TTS_PIPER_ENDPOINTS_EN"] =
                ReferenceExpression.Create($"{enEp}");
        })
        .WithEnvironment(ctx =>
        {
            var frEp = piperFrContainer!.GetEndpoint("piper-fr");
            ctx.EnvironmentVariables["SWITCHYARD_TTS_PIPER_ENDPOINTS_FR"] =
                ReferenceExpression.Create($"{frEp}");
        })
        // Fallback endpoint (English) for unknown languages
        .WithEnvironment(ctx =>
        {
            var enEp = piperEnContainer!.GetEndpoint("piper-en");
            ctx.EnvironmentVariables["SWITCHYARD_TTS_PIPER_ENDPOINT"] =
                ReferenceExpression.Create($"{enEp}");
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
// Test Client — React + ASP.NET Core BFF for interactive testing
// ---------------------------------------------------------------------------
var testClient = builder.AddProject<Projects.Switchyard_TestClient>("testclient")
    .WithExternalHttpEndpoints()
    .WithEnvironment(ctx =>
    {
        var httpEp = switchyard.GetEndpoint("http");
        var grpcEp = switchyard.GetEndpoint("grpc");
        ctx.EnvironmentVariables["services__switchyard__http__0"] =
            ReferenceExpression.Create($"{httpEp}");
        ctx.EnvironmentVariables["services__switchyard__grpc__0"] =
            ReferenceExpression.Create($"{grpcEp}");
    });

// ---------------------------------------------------------------------------
// Future: add dependent services here
// ---------------------------------------------------------------------------
// var redis = builder.AddRedis("cache");
// switchyard.WithReference(redis);

// var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2")
//     .WithEndpoint(port: 1883, name: "mqtt");

builder.Build().Run();
