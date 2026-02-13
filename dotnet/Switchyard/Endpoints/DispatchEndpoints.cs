using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
using Switchyard.Dispatch;
using Switchyard.Models;

namespace Switchyard.Endpoints;

/// <summary>Minimal API endpoint mappings for the Switchyard HTTP surface.</summary>
public static class DispatchEndpoints
{
    public static RouteGroupBuilder MapDispatchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("")
            .WithOpenApi();

        group.MapPost("/dispatch", HandleDispatchAsync)
            .WithName("Dispatch")
            .WithSummary("Process a voice/text dispatch message through the interpreter pipeline")
            .Accepts<DispatchMessage>("application/json")
            .Produces<DispatchResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<Results<Ok<DispatchResult>, ProblemHttpResult>> HandleDispatchAsync(
        HttpContext ctx,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var msg = new DispatchMessage();
        var contentType = ctx.Request.ContentType ?? "";

        if (contentType.Contains("application/json"))
        {
            msg = await ctx.Request.ReadFromJsonAsync(SwitchyardJsonContext.Default.DispatchMessage, ct) ?? new();
        }
        else
        {
            // Raw audio body.
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ct);
            msg.Audio = ms.ToArray();
            msg.ContentType = contentType;
            msg.Source = ctx.Request.Headers["X-Switchyard-Source"].FirstOrDefault() ?? "";

            var instrHeader = ctx.Request.Headers["X-Switchyard-Instruction"].FirstOrDefault();
            if (!string.IsNullOrEmpty(instrHeader))
                msg.Instruction = JsonSerializer.Deserialize(instrHeader, SwitchyardJsonContext.Default.Instruction) ?? new();
        }

        var result = await dispatcher.HandleAsync(msg, ct);

        if (!string.IsNullOrEmpty(result.Error))
            return TypedResults.Problem(result.Error, statusCode: StatusCodes.Status500InternalServerError);

        return TypedResults.Ok(result);
    }

    /// <summary>Maps standard health-check endpoints using the built-in health checks middleware.</summary>
    public static IEndpointRouteBuilder MapSwitchyardHealthChecks(this IEndpointRouteBuilder routes)
    {
        routes.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => false, // Liveness — always healthy if the process is up.
            ResponseWriter = WriteHealthResponse
        });

        routes.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse
        });

        return routes;
    }

    private static Task WriteHealthResponse(HttpContext ctx, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy ? "ok" : "degraded";
        return ctx.Response.WriteAsJsonAsync(new { status }, cancellationToken: ctx.RequestAborted);
    }
}
