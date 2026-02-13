// Switchyard Test Client — ASP.NET Core BFF with React SPA.
// Proxies audio dispatch requests to the Switchyard daemon via HTTP REST and gRPC,
// providing a browser-based UI for recording / sending audio and playing back responses.

using Switchyard.TestClient.Endpoints;
using Switchyard.V1;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Switchyard HTTP client (service discovery via Aspire or fallback)
// ---------------------------------------------------------------------------
var switchyardHttp = builder.Configuration["services__switchyard__http__0"]
    ?? builder.Configuration["SwitchyardHttpUrl"]
    ?? "http://localhost:8080";

builder.Services.AddHttpClient("switchyard", client =>
{
    client.BaseAddress = new Uri(switchyardHttp);
    client.Timeout = TimeSpan.FromSeconds(120);
});

// ---------------------------------------------------------------------------
// Switchyard gRPC channel
// ---------------------------------------------------------------------------
var switchyardGrpc = builder.Configuration["services__switchyard__grpc__0"]
    ?? builder.Configuration["SwitchyardGrpcUrl"]
    ?? "http://localhost:50051";

builder.Services.AddSingleton(_ =>
{
    var channel = Grpc.Net.Client.GrpcChannel.ForAddress(switchyardGrpc);
    return new SwitchyardService.SwitchyardServiceClient(channel);
});

// ---------------------------------------------------------------------------
// Health checks
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------------
app.UseDefaultFiles();
app.UseStaticFiles();

// Map API endpoints
app.MapDispatchEndpoints();
app.MapHealthEndpoints(switchyardHttp);
app.MapHealthChecks("/health");

// SPA fallback — serve index.html for client-side routes
app.MapFallbackToFile("index.html");

app.Run();
