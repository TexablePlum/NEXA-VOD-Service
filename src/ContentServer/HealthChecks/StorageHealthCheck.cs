using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nexa.ContentServer.HealthChecks

{
    /// <summary>
    /// Health check sprawdzający dostępność folderu storage.
    /// </summary>
    public class StorageHealthCheck : IHealthCheck
    {
        private readonly string _storagePath;
        private readonly ILogger<StorageHealthCheck> _logger;

        public StorageHealthCheck(IConfiguration configuration, ILogger<StorageHealthCheck> logger)
        {
            _storagePath = configuration["ContentStorage:BasePath"] ?? "./content/storage";
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var fullPath = Path.GetFullPath(_storagePath);

                if (Directory.Exists(fullPath))
                {
                    _logger.LogDebug("Storage health check passed: {Path}", fullPath);
                    return Task.FromResult(HealthCheckResult.Healthy($"Storage accessible at {fullPath}"));
                }

                _logger.LogWarning("Storage health check failed: Directory not found at {Path}", fullPath);

                return Task.FromResult(HealthCheckResult.Unhealthy($"Storage not found at {fullPath}"));
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage health check threw exception");
                return Task.FromResult(HealthCheckResult.Unhealthy($"Storage check failed: {ex.Message}", ex));
            }
        }
    }
}