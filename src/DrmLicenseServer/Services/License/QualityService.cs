using Nexa.Shared.Constants;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do zarządzania dostępnymi jakościami wideo dla contentu.
/// Implementuje cache'owanie w Redis.
/// </summary>
public class QualityService
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<QualityService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Redis key prefix dla jakości (używane zarówno dla SET jak i cache)
    private const string QualitiesPrefix = "content:qualities:";

    public QualityService(
        IDatabase redisDb,
        ILogger<QualityService> logger)
    {
        _redisDb = redisDb;
        _logger = logger;
    }

    /// <summary>
    /// Pobiera listę rzeczywiście dostępnych jakości dla contentu,
    /// przefiltrowaną przez plan użytkownika i TPM urządzenia.
    /// Urządzenia bez TPM są ograniczone do maksymalnie 720p niezależnie od planu.
    /// Wyniki są cache'owane w Redis (TTL: 3600s).
    /// </summary>
    public async Task<List<string>> GetAvailableQualitiesAsync(
        string contentId,
        string userPlan,
        bool hasTpm,
        CancellationToken ct = default)
    {
        // Cache key zawiera informację o TPM
        var cacheKey = $"{QualitiesPrefix}{contentId}:{userPlan}:{(hasTpm ? "tpm" : "notpm")}";
        var cachedJson = await _redisDb.StringGetAsync(cacheKey);

        if (cachedJson.HasValue)
        {
            var cached = JsonSerializer.Deserialize<List<string>>(cachedJson.ToString(), _jsonOptions);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for qualities: {ContentId}, plan: {Plan}, TPM: {HasTpm}",
                    contentId, userPlan, hasTpm);
                return cached;
            }
        }

        _logger.LogDebug("Cache miss for qualities: {ContentId}, plan: {Plan}, TPM: {HasTpm} - fetching from Redis SET",
            contentId, userPlan, hasTpm);

        // Pobiera maksymalną jakość dla planu użytkownika
        var maxQuality = Plans.GetMaxQuality(userPlan);

        if (!hasTpm && Qualities.GetLevel(maxQuality) > Qualities.GetLevel("720p"))
        {
            maxQuality = "720p";
            _logger.LogDebug("Max quality limited to 720p due to missing TPM attestation");
        }

        // Format SET key: content:qualities:{contentId}
        var qualitiesSetKey = $"{QualitiesPrefix}{contentId}";
        var allQualities = await _redisDb.SetMembersAsync(qualitiesSetKey);

        var result = new List<string>();

        foreach (var qualityValue in allQualities)
        {
            var quality = qualityValue.ToString();

            // Sprawdza czy ta jakość jest dozwolona dla planu użytkownika
            if (Qualities.IsValid(quality) &&
                Qualities.IsQualitySufficient(maxQuality, quality))
            {
                result.Add(quality);
            }
        }

        // Sortuje według hierarchii jakości (od najniższej do najwyższej)
        result.Sort((a, b) => Qualities.GetLevel(a).CompareTo(Qualities.GetLevel(b)));

        // Zapisuje w cache (TTL: 3600s = 1 godzina)
        var resultJson = JsonSerializer.Serialize(result, _jsonOptions);
        await _redisDb.StringSetAsync(cacheKey, resultJson, TimeSpan.FromSeconds(3600));

        _logger.LogDebug("Cached qualities for {ContentId}, plan: {Plan}: {Qualities}",
            contentId, userPlan, string.Join(", ", result));

        return result;
    }

    /// <summary>
    /// Dodaje jakość do listy dostępnych dla contentu.
    /// Używane podczas importu CEK.
    /// </summary>
    public async Task AddQualityAsync(string contentId, string quality, CancellationToken ct = default)
    {
        var qualitiesSetKey = $"{QualitiesPrefix}{contentId}";
        await _redisDb.SetAddAsync(qualitiesSetKey, quality);

        _logger.LogDebug("Added quality {Quality} to content {ContentId}", quality, contentId);
    }
}
