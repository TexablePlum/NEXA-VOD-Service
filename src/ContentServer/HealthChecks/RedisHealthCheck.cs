using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Nexa.ContentServer.HealthChecks
{
    /// <summary>
    /// Health check dla Redis.
    /// </summary>
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisHealthCheck(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (_redis.IsConnected)
                {
                    return Task.FromResult(
                        HealthCheckResult.Healthy("Redis is connected"));
                }

                return Task.FromResult(
                    HealthCheckResult.Degraded("Redis is disconnected but service can work without it"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(
                    HealthCheckResult.Degraded($"Redis check failed: {ex.Message}"));
            }
        }
    }
}