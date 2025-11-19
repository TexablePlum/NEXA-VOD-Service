using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis do zapisywania audit log-ów licencji.
/// </summary>
public class AuditService
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<AuditService> _logger;
    private const string AuditStreamKey = "license:audit";
    private const int MaxAuditEntries = 10000;

    public AuditService(IDatabase redisDb, ILogger<AuditService> logger)
    {
        _redisDb = redisDb;
        _logger = logger;
    }

    /// <summary>
    /// Loguje wydanie licencji.
    /// </summary>
    public async Task LogLicenseIssuedAsync(
        string userId,
        string contentId,
        string quality,
        string userPlan,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        try
        {
            var entries = new NameValueEntry[]
            {
                new("action", "issued"),
                new("userId", userId),
                new("contentId", contentId),
                new("quality", quality),
                new("userPlan", userPlan),
                new("expiresAt", expiresAt.ToString("o")), // ISO 8601
                new("timestamp", DateTime.UtcNow.ToString("o"))
            };

            await _redisDb.StreamAddAsync(AuditStreamKey, entries, maxLength: MaxAuditEntries, useApproximateMaxLength: true);

            _logger.LogDebug(
                "Audit: License issued - user {UserId}, content {ContentId}, quality {Quality}",
                userId, contentId, quality);
        }
        catch (Exception ex)
        {
            // Audit log nie powinien blokować głównej operacji
            _logger.LogError(ex, "Failed to write audit log for license issued");
        }
    }

    /// <summary>
    /// Loguje odnowienie licencji.
    /// </summary>
    public async Task LogLicenseRenewedAsync(
        string userId,
        string contentId,
        string quality,
        string userPlan,
        DateTime newExpiresAt,
        CancellationToken ct = default)
    {
        try
        {
            var entries = new NameValueEntry[]
            {
                new("action", "renewed"),
                new("userId", userId),
                new("contentId", contentId),
                new("quality", quality),
                new("userPlan", userPlan),
                new("newExpiresAt", newExpiresAt.ToString("o")),
                new("timestamp", DateTime.UtcNow.ToString("o"))
            };

            await _redisDb.StreamAddAsync(AuditStreamKey, entries, maxLength: MaxAuditEntries, useApproximateMaxLength: true);

            _logger.LogDebug(
                "Audit: License renewed - user {UserId}, content {ContentId}, quality {Quality}",
                userId, contentId, quality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for license renewed");
        }
    }

    /// <summary>
    /// Loguje cofnięcie (revoke) licencji.
    /// </summary>
    public async Task LogLicenseRevokedAsync(
        string userId,
        string contentId,
        string quality,
        string userPlan,
        CancellationToken ct = default)
    {
        try
        {
            var entries = new NameValueEntry[]
            {
                new("action", "revoked"),
                new("userId", userId),
                new("contentId", contentId),
                new("quality", quality),
                new("userPlan", userPlan),
                new("timestamp", DateTime.UtcNow.ToString("o"))
            };

            await _redisDb.StreamAddAsync(AuditStreamKey, entries, maxLength: MaxAuditEntries, useApproximateMaxLength: true);

            _logger.LogDebug(
                "Audit: License revoked - user {UserId}, content {ContentId}, quality {Quality}",
                userId, contentId, quality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for license revoked");
        }
    }

    /// <summary>
    /// Loguje odrzucenie żądania licencji (forbidden/not found).
    /// </summary>
    public async Task LogLicenseRejectedAsync(
        string userId,
        string contentId,
        string quality,
        string reason,
        string errorCode,
        CancellationToken ct = default)
    {
        try
        {
            var entries = new NameValueEntry[]
            {
                new("action", "rejected"),
                new("userId", userId),
                new("contentId", contentId),
                new("quality", quality),
                new("reason", reason),
                new("errorCode", errorCode),
                new("timestamp", DateTime.UtcNow.ToString("o"))
            };

            await _redisDb.StreamAddAsync(AuditStreamKey, entries, maxLength: MaxAuditEntries, useApproximateMaxLength: true);

            _logger.LogDebug(
                "Audit: License rejected - user {UserId}, content {ContentId}, reason {Reason}",
                userId, contentId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for license rejected");
        }
    }

    /// <summary>
    /// Pobiera ostatnie N wpisów z audit log (do celów administracyjnych).
    /// </summary>
    public async Task<List<Dictionary<string, string>>> GetRecentAuditEntriesAsync(
        int count = 100,
        CancellationToken ct = default)
    {
        var result = new List<Dictionary<string, string>>();

        try
        {
            // StreamRangeAsync pozwala na sortowanie
            var entries = await _redisDb.StreamRangeAsync(
                AuditStreamKey,
                minId: "-",  // od początku
                maxId: "+",  // do końca
                count: count,
                messageOrder: Order.Descending);

            foreach (var entry in entries)
            {
                var dict = new Dictionary<string, string>
                {
                    ["id"] = entry.Id.ToString()
                };

                foreach (var field in entry.Values)
                {
                    dict[field.Name.ToString()] = field.Value.ToString();
                }

                result.Add(dict);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read audit log");
        }

        return result;
    }
}
