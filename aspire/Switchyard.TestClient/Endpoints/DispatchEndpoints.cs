using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Switchyard.TestClient.Models;
using Switchyard.V1;

namespace Switchyard.TestClient.Endpoints;

public static class DispatchEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Registers dispatch proxy endpoints.
    /// </summary>
    public static void MapDispatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dispatch").WithTags("Dispatch");

        group.MapPost("/http", HandleHttpDispatch)
             .DisableAntiforgery()
             .WithName("DispatchViaHttp")
             .WithDescription("Proxy dispatch to Switchyard via HTTP REST");

        group.MapPost("/grpc", HandleGrpcDispatch)
             .DisableAntiforgery()
             .WithName("DispatchViaGrpc")
             .WithDescription("Proxy dispatch to Switchyard via gRPC");
    }

    /// <summary>
    /// Registers health proxy endpoint.
    /// </summary>
    public static void MapHealthEndpoints(this WebApplication app, string switchyardBaseUrl)
    {
        app.MapGet("/api/switchyard/health", async (IHttpClientFactory factory) =>
        {
            try
            {
                var client = factory.CreateClient("switchyard");
                // Health endpoint is on port 8081 — construct URL relative to known base
                var healthUrl = switchyardBaseUrl.Replace(":8080", ":8081");
                using var healthClient = new HttpClient { BaseAddress = new Uri(healthUrl) };
                var resp = await healthClient.GetAsync("/healthz");
                return Results.Ok(new { status = resp.IsSuccessStatusCode ? "healthy" : "unhealthy", code = (int)resp.StatusCode });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { status = "unreachable", error = ex.Message });
            }
        });
    }

    // -----------------------------------------------------------------------
    // HTTP REST dispatch — proxy multipart or JSON to Switchyard POST /dispatch
    // -----------------------------------------------------------------------
    private static async Task<IResult> HandleHttpDispatch(
        HttpRequest request,
        IHttpClientFactory factory)
    {
        var client = factory.CreateClient("switchyard");

        try
        {
            HttpResponseMessage response;

            if (request.HasFormContentType)
            {
                // Multipart form: audio file + optional instruction JSON
                var form = await request.ReadFormAsync();
                var audioFile = form.Files.GetFile("audio");
                var instructionJson = form["instruction"].FirstOrDefault();

                if (audioFile is null || audioFile.Length == 0)
                    return Results.BadRequest(new { error = "No audio file provided" });

                // Send as raw audio with headers (Switchyard's preferred raw-audio path)
                using var audioStream = audioFile.OpenReadStream();
                using var ms = new MemoryStream();
                await audioStream.CopyToAsync(ms);
                var audioBytes = ms.ToArray();

                using var content = new ByteArrayContent(audioBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(
                    audioFile.ContentType ?? "audio/webm");

                using var msg = new HttpRequestMessage(HttpMethod.Post, "/dispatch")
                {
                    Content = content
                };
                msg.Headers.Add("X-Switchyard-Source", "testclient");
                if (!string.IsNullOrEmpty(instructionJson))
                    msg.Headers.Add("X-Switchyard-Instruction", instructionJson);

                response = await client.SendAsync(msg);
            }
            else
            {
                // JSON body — pass through directly
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();

                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                response = await client.PostAsync("/dispatch", content);
            }

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Results.Json(
                    new { error = $"Switchyard returned {(int)response.StatusCode}", detail = json },
                    statusCode: (int)response.StatusCode);

            // Parse and re-serialize to ensure consistent casing
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            return Results.Json(result, statusCode: 200);
        }
        catch (HttpRequestException ex)
        {
            return Results.Json(
                new { error = "Failed to reach Switchyard", detail = ex.Message },
                statusCode: 502);
        }
    }

    // -----------------------------------------------------------------------
    // gRPC dispatch — use generated client to call Switchyard.Dispatch RPC
    // -----------------------------------------------------------------------
    private static async Task<IResult> HandleGrpcDispatch(
        HttpRequest request,
        SwitchyardService.SwitchyardServiceClient grpcClient)
    {
        try
        {
            DispatchRequest grpcRequest;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync();
                var audioFile = form.Files.GetFile("audio");
                var instructionJson = form["instruction"].FirstOrDefault();

                grpcRequest = new DispatchRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    Source = "testclient"
                };

                if (audioFile is not null && audioFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await audioFile.OpenReadStream().CopyToAsync(ms);
                    grpcRequest.Audio = ByteString.CopyFrom(ms.ToArray());
                    grpcRequest.ContentType = audioFile.ContentType ?? "audio/webm";
                }

                if (!string.IsNullOrEmpty(instructionJson))
                {
                    var inst = JsonSerializer.Deserialize<InstructionDto>(instructionJson, JsonOpts);
                    if (inst is not null)
                        grpcRequest.Instruction = MapInstruction(inst);
                }
            }
            else
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var dto = JsonSerializer.Deserialize<DispatchRequestDto>(body, JsonOpts);

                grpcRequest = new DispatchRequest
                {
                    Id = dto?.Id ?? Guid.NewGuid().ToString(),
                    Source = dto?.Source ?? "testclient",
                    Text = dto?.Text ?? ""
                };

                if (!string.IsNullOrEmpty(dto?.Audio))
                {
                    grpcRequest.Audio = ByteString.CopyFrom(Convert.FromBase64String(dto.Audio));
                    grpcRequest.ContentType = dto.ContentType ?? "audio/webm";
                }

                if (dto?.Instruction is not null)
                    grpcRequest.Instruction = MapInstruction(dto.Instruction);
            }

            var grpcResponse = await grpcClient.DispatchAsync(grpcRequest);

            var result = new DispatchResultDto
            {
                MessageId = grpcResponse.MessageId,
                Transcript = grpcResponse.Transcript,
                Language = grpcResponse.Language,
                ResponseText = grpcResponse.ResponseText,
                ResponseAudio = grpcResponse.ResponseAudio,
                ResponseContentType = grpcResponse.ResponseContentType,
                Error = string.IsNullOrEmpty(grpcResponse.Error) ? null : grpcResponse.Error,
                Commands = grpcResponse.Commands.Select(c => new CommandDto
                {
                    Action = c.Action,
                    Raw = c.ParamsJson
                }).ToList(),
                RoutedTo = grpcResponse.RoutedTo.ToList()
            };

            return Results.Json(result, JsonOpts);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unimplemented)
        {
            return Results.Json(
                new { error = "gRPC Dispatch RPC is not yet implemented on the Switchyard server", grpcStatus = "UNIMPLEMENTED" },
                statusCode: 501);
        }
        catch (Grpc.Core.RpcException ex)
        {
            return Results.Json(
                new { error = $"gRPC error: {ex.Status.Detail}", grpcStatus = ex.StatusCode.ToString() },
                statusCode: 502);
        }
        catch (Exception ex)
        {
            return Results.Json(
                new { error = "Failed to reach Switchyard via gRPC", detail = ex.Message },
                statusCode: 502);
        }
    }

    private static Instruction MapInstruction(InstructionDto dto)
    {
        var inst = new Instruction
        {
            CommandFormat = dto.CommandFormat ?? "",
            Prompt = dto.Prompt ?? "",
            ResponseMode = dto.ResponseMode ?? "text+audio"
        };

        if (dto.Targets is not null)
        {
            foreach (var t in dto.Targets)
            {
                inst.Targets.Add(new Target
                {
                    ServiceName = t.ServiceName ?? "",
                    Endpoint = t.Endpoint ?? "",
                    Protocol = t.Protocol ?? "",
                    FormatTemplate = t.FormatTemplate ?? ""
                });
            }
        }

        return inst;
    }
}
