using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Switchyard.Health;

/// <summary>
/// Custom health check that reports readiness once the application has fully started.
/// Integrates with the ASP.NET Core health checks infrastructure.
/// </summary>
public sealed class SwitchyardHealthCheck : IHealthCheck
{
    private volatile bool _isReady;

    public void MarkReady() => _isReady = true;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isReady
            ? HealthCheckResult.Healthy("Switchyard is ready.")
            : HealthCheckResult.Unhealthy("Switchyard is not yet ready."));
    }
}
