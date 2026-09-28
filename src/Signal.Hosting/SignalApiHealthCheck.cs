using Microsoft.Extensions.Diagnostics.HealthChecks;
using Signal.Application.Abstractions;

namespace Signal.Hosting;

/// <summary>
/// Reports the health of the signal-cli-rest-api container via <see cref="ISystemService.IsHealthyAsync"/>.
/// Registered with <c>AddHealthChecks().AddSignalApi()</c>.
/// </summary>
internal sealed class SignalApiHealthCheck(ISystemService system) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await system.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("signal-cli-rest-api is reachable.")
            : new HealthCheckResult(context.Registration.FailureStatus, "signal-cli-rest-api is not healthy or unreachable.");
}
