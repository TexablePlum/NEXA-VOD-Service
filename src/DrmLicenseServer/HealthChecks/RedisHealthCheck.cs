using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Nexa.DrmLicenseServer.HealthChecks;

/// <summary>
/// Health check dla Redis.
/// Sprawdza czy połączenie z Redis jest aktywne.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            var pingTime = await db.PingAsync();

            if (pingTime.TotalMilliseconds > 1000)
            {
                return HealthCheckResult.Degraded(
                    $"Redis is slow (ping: {pingTime.TotalMilliseconds:F2}ms)");
            }

            return HealthCheckResult.Healthy(
                $"Redis is healthy (ping: {pingTime.TotalMilliseconds:F2}ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis connection failed",
                ex);
        }
    }
}
