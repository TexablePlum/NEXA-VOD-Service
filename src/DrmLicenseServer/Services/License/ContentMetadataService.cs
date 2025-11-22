using Nexa.Shared.Constants;
using Nexa.Shared.Exceptions;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do zarządzania metadanymi contentu w Redis.
/// </summary>
public class ContentMetadataService
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<ContentMetadataService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string ContentMetaPrefix = "content:meta:";

    public ContentMetadataService(
        IDatabase redisDb,
        ILogger<ContentMetadataService> logger)
    {
        _redisDb = redisDb;
        _logger = logger;
    }

    /// <summary>
    /// Pobiera metadane contentu z Redis.
    /// </summary>
    public async Task<ContentMetadata?> GetMetadataAsync(string contentId, CancellationToken ct = default)
    {
        var metaKey = $"{ContentMetaPrefix}{contentId}";
        var metaJson = await _redisDb.StringGetAsync(metaKey);

        if (!metaJson.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ContentMetadata>(metaJson.ToString(), _jsonOptions);
    }

    /// <summary>
    /// Importuje metadane contentu (RequiredPlan i ReleaseDate).
    /// </summary>
    public async Task ImportMetadataAsync(
        string contentId,
        string requiredPlan,
        DateTime? releaseDate = null,
        CancellationToken ct = default)
    {
        if (!Plans.IsValid(requiredPlan))
        {
            throw new ValidationException($"Nieprawidłowy plan: {requiredPlan}");
        }

        var meta = new ContentMetadata
        {
            RequiredPlan = requiredPlan,
            ReleaseDate = releaseDate
        };

        var metaKey = $"{ContentMetaPrefix}{contentId}";
        var metaJson = JsonSerializer.Serialize(meta, _jsonOptions);

        await _redisDb.StringSetAsync(metaKey, metaJson);

        _logger.LogInformation("Imported content metadata for {ContentId}, requiredPlan: {RequiredPlan}, releaseDate: {ReleaseDate}",
            contentId, requiredPlan, releaseDate?.ToString("yyyy-MM-dd") ?? "none");
    }

    /// <summary>
    /// Model metadanych contentu.
    /// </summary>
    public class ContentMetadata
    {
        public string RequiredPlan { get; set; } = Plans.FREE;
        public DateTime? ReleaseDate { get; set; }
    }
}
