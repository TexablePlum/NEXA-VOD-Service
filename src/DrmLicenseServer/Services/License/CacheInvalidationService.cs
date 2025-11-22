using StackExchange.Redis;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do inwalidacji cache'u Redis.
/// Centralizuje logikę cache invalidation dla całej aplikacji.
/// </summary>
public class CacheInvalidationService
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<CacheInvalidationService> _logger;

    public CacheInvalidationService(
        IDatabase redisDb,
        ILogger<CacheInvalidationService> logger)
    {
        _redisDb = redisDb;
        _logger = logger;
    }

    /// <summary>
    /// Invaliduje cache dla jakości contentu.
    /// Usuwa wszystkie klucze cache pasujące do wzorca: content:qualities:{contentId}:*
    /// </summary>
    public async Task InvalidateQualityCacheAsync(string contentId, CancellationToken ct = default)
    {
        var cachePattern = $"content:qualities:{contentId}:*";

        try
        {
            var endpoints = _redisDb.Multiplexer.GetEndPoints();
            if (endpoints.Length == 0)
            {
                _logger.LogWarning("No Redis endpoints available for cache invalidation");
                return;
            }

            var server = _redisDb.Multiplexer.GetServer(endpoints[0]);
            var keysToDelete = server.Keys(pattern: cachePattern).ToArray();

            if (keysToDelete.Length > 0)
            {
                await _redisDb.KeyDeleteAsync(keysToDelete);
                _logger.LogInformation(
                    "Quality cache invalidated for ContentId: {ContentId} - deleted {Count} keys",
                    contentId,
                    keysToDelete.Length
                );
            }
            else
            {
                _logger.LogDebug("No quality cache keys found for ContentId: {ContentId}", contentId);
            }
        }
        catch (Exception ex)
        {
            // Cache invalidation failure nie powinno blokować operacji
            _logger.LogWarning(ex, "Failed to invalidate quality cache for ContentId: {ContentId}", contentId);
        }
    }

    /// <summary>
    /// Invaliduje cache metadanych contentu (RequiredPlan).
    /// Usuwa klucz: content:meta:{contentId}
    /// </summary>
    public async Task InvalidateMetadataCacheAsync(string contentId, CancellationToken ct = default)
    {
        var metaKey = $"content:meta:{contentId}";

        try
        {
            var deleted = await _redisDb.KeyDeleteAsync(metaKey);

            if (deleted)
            {
                _logger.LogInformation(
                    "Metadata cache invalidated for ContentId: {ContentId}",
                    contentId
                );
            }
            else
            {
                _logger.LogDebug("No metadata cache found for ContentId: {ContentId}", contentId);
            }
        }
        catch (Exception ex)
        {
            // Cache invalidation failure nie powinno blokować operacji
            _logger.LogWarning(ex, "Failed to invalidate metadata cache for ContentId: {ContentId}", contentId);
        }
    }

    /// <summary>
    /// Invaliduje wszystkie cache'e związane z contentem (qualities, metadata).
    /// błędy nie przerwą operacji.
    /// </summary>
    public async Task InvalidateContentCacheAsync(string contentId, CancellationToken ct = default)
    {
        await InvalidateQualityCacheAsync(contentId, ct);
        await InvalidateMetadataCacheAsync(contentId, ct);
    }
}
