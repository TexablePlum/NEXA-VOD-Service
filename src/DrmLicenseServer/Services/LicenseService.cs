using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using Nexa.Shared.Constants;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Nexa.DrmLicenseServer.Validation;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis zarządzania licencjami DRM (CEK).
/// Zwraca klucze deszyfrujące dla uprawnionych użytkowników.
/// Wydane licencje przechowywane w PostgreSQL, CEK i metadata w Redis.
/// </summary>
public class LicenseService
{
    private readonly IDatabase _redisDb;
    private readonly NexaDbContext _dbContext;
    private readonly ILogger<LicenseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AuditService _auditService;
    private readonly CekEncryptionService _cekEncryption;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string CekKeyPrefix = "cek:";
    private const string ContentMetaPrefix = "content:meta:";
    private const string AvailableQualitiesSetPrefix = "content:qualities:"; // Redis SET dla dostępnych jakości

    public LicenseService(
        IDatabase redisDb,
        NexaDbContext dbContext,
        ILogger<LicenseService> logger,
        IConfiguration configuration,
        AuditService auditService,
        CekEncryptionService cekEncryption)
    {
        _redisDb = redisDb;
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
        _auditService = auditService;
        _cekEncryption = cekEncryption;
    }

    /// <summary>
    /// Pobiera licencję (CEK) dla contentu i sprawdza uprawnienia użytkownika.
    /// </summary>
    public async Task<LicenseResponse> GetLicenseAsync(
        string contentId,
        string quality,
        User user,
        CancellationToken ct = default)
    {
        // Walidacja parametrów
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        // Path traversal protection
        if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
        {
            _logger.LogWarning("Path traversal attempt blocked in license request: {ContentId}", contentId);
            throw new ValidationException("Nieprawidłowy format Content ID.");
        }

        if (string.IsNullOrWhiteSpace(quality))
        {
            throw new ValidationException("Quality nie może być pusta.");
        }

        // Walidacja quality format
        if (!Qualities.IsValid(quality))
        {
            _logger.LogWarning("Invalid quality format in license request: {Quality}", quality);
            throw new ValidationException($"Nieprawidłowa jakość: {quality}. Dozwolone: 480p, 720p, 1080p, 1440p, 2160p");
        }

        // Pobiera metadane contentu
        var contentMeta = await GetContentMetadataAsync(contentId, ct);

        if (contentMeta == null)
        {
            _logger.LogWarning("Content metadata not found for contentId: {ContentId}", contentId);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, quality,
                "Content not found",
                ErrorCode.CONTENT_NOT_FOUND,
                ct);

            throw new NotFoundException(
                $"Content o ID '{contentId}' nie został znaleziony.",
                contentId
            );
        }

        // Sprawdza uprawnienia do contentu
        if (!Plans.HasSufficientPlan(user.Plan, contentMeta.RequiredPlan))
        {
            _logger.LogWarning(
                "Insufficient plan for user {UserId} (plan: {UserPlan}) to access content {ContentId} (required: {RequiredPlan})",
                user.UserId, user.Plan, contentId, contentMeta.RequiredPlan);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, quality,
                $"Insufficient plan: {user.Plan} < {contentMeta.RequiredPlan}",
                ErrorCode.FORBIDDEN,
                ct);

            throw new ForbiddenException(
                $"Twój plan ({user.Plan}) nie pozwala na odtworzenie tego contentu. Wymagany plan: {contentMeta.RequiredPlan}",
                new Dictionary<string, object>
                {
                    ["userPlan"] = user.Plan,
                    ["requiredPlan"] = contentMeta.RequiredPlan,
                    ["contentId"] = contentId
                }
            );
        }

        // Sprawdza limit konkurencyjnych stream-ów
        await CheckConcurrentStreamLimitAsync(user.UserId, contentId, quality, user.Plan, ct);

        // Pobiera rzeczywiście dostępne jakości dla tego contentu i planu użytkownika
        var availableQualities = await GetAvailableQualitiesAsync(contentId, user.Plan, ct);

        if (availableQualities.Count == 0)
        {
            _logger.LogWarning("No qualities available for content {ContentId} and user plan {Plan}",
                contentId, user.Plan);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, quality,
                $"No qualities available for plan: {user.Plan}",
                ErrorCode.CONTENT_NOT_FOUND,
                ct);

            throw new NotFoundException(
                $"Content '{contentId}' nie ma dostępnych jakości dla Twojego planu ({user.Plan}).",
                contentId
            );
        }

        // Sprawdza czy żądana jakość jest dostępna dla tego contentu i planu użytkownika
        if (!availableQualities.Contains(quality))
        {
            var maxQuality = Plans.GetMaxQuality(user.Plan);

            _logger.LogWarning(
                "Quality {Quality} not available for user {UserId} (plan: {UserPlan}) on content {ContentId}. Available: {Available}",
                quality, user.UserId, user.Plan, contentId, string.Join(", ", availableQualities));

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, quality,
                $"Quality {quality} not available. Available: {string.Join(", ", availableQualities)}",
                ErrorCode.FORBIDDEN,
                ct);

            throw new ForbiddenException(
                $"Jakość '{quality}' nie jest dostępna dla tego contentu. Dostępne jakości dla Twojego planu ({user.Plan}): {string.Join(", ", availableQualities)}",
                new Dictionary<string, object>
                {
                    ["userPlan"] = user.Plan,
                    ["requestedQuality"] = quality,
                    ["maxQualityForPlan"] = maxQuality,
                    ["availableQualities"] = availableQualities,
                    ["contentId"] = contentId
                }
            );
        }

        // Pobiera CEK dla tej jakości (zaszyfrowany w Redis)
        var cekKey = $"{CekKeyPrefix}{contentId}:{quality}";
        var cekJson = await _redisDb.StringGetAsync(cekKey);

        if (!cekJson.HasValue)
        {
            _logger.LogWarning("CEK not found for {ContentId} quality {Quality}", contentId, quality);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, quality,
                "CEK not found in Redis",
                ErrorCode.CONTENT_NOT_FOUND,
                ct);

            throw new NotFoundException(
                $"Licencja dla contentu '{contentId}' w jakości '{quality}' nie została znaleziona.",
                $"{contentId}:{quality}"
            );
        }

        var encryptedCekData = JsonSerializer.Deserialize<EncryptedCekData>(cekJson.ToString(), _jsonOptions);

        if (encryptedCekData == null)
        {
            throw new InternalServerException("Failed to deserialize CEK data.");
        }

        // Deszyfruje CEK za pomocą master key-a
        string decryptedKey;
        try
        {
            decryptedKey = _cekEncryption.Decrypt(encryptedCekData.EncryptedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt CEK for {ContentId}:{Quality}", contentId, quality);
            throw new InternalServerException("Failed to decrypt license key. Please contact support.");
        }

        // Oblicza czas wygaśnięcia licencji
        var expirationHours = _configuration.GetValue<int>("License:ExpirationHours", 8);
        var expiresAt = DateTime.UtcNow.AddHours(expirationHours);

        _logger.LogInformation(
            "License issued for user {UserId} (plan: {Plan}) - content {ContentId} quality {Quality}, expires at {ExpiresAt}",
            user.UserId, user.Plan, contentId, quality, expiresAt);

        // Audit log
        await _auditService.LogLicenseIssuedAsync(
            user.UserId, contentId, quality, user.Plan, expiresAt, ct);

        // Zapisuje informację o wydanej licencji
        await SaveIssuedLicenseAsync(user.UserId, contentId, quality, expiresAt, ct);

        return new LicenseResponse
        {
            Key = decryptedKey,
            KeyId = encryptedCekData.KeyId,
            Quality = quality,
            ContentId = contentId,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Odnawia licencję (CEK) dla contentu. Sprawdza czy odnowienie jest możliwe
    /// w oparciu o RenewalThresholdMinutes.
    /// </summary>
    public async Task<LicenseResponse> RenewLicenseAsync(
        string contentId,
        string quality,
        User user,
        CancellationToken ct = default)
    {
        // Sprawdza czy istnieje poprzednia licencja w bazie danych
        var licenseEntity = await _dbContext.IssuedLicenses
            .FirstOrDefaultAsync(l =>
                l.UserId == user.UserId &&
                l.ContentId == contentId &&
                l.Quality == quality &&
                l.ExpiresAt > DateTime.UtcNow, ct);

        if (licenseEntity != null)
        {
            // Licencja istnieje i jest nadal ważna
            var renewalThresholdMinutes = _configuration.GetValue<int>("License:RenewalThresholdMinutes", 30);
            var timeLeft = licenseEntity.ExpiresAt - DateTime.UtcNow;

            if (timeLeft.TotalMinutes > renewalThresholdMinutes)
            {
                _logger.LogWarning(
                    "License renewal too early for user {UserId} - content {ContentId} quality {Quality}. Time left: {TimeLeft}min, threshold: {Threshold}min",
                    user.UserId, contentId, quality, (int)timeLeft.TotalMinutes, renewalThresholdMinutes);

                throw new ValidationException(
                    $"Licencja jest nadal ważna przez {(int)timeLeft.TotalMinutes} minut. " +
                    $"Odnowienie możliwe dopiero za {(int)(timeLeft.TotalMinutes - renewalThresholdMinutes)} minut.",
                    new Dictionary<string, object>
                    {
                        ["currentExpiresAt"] = licenseEntity.ExpiresAt,
                        ["timeLeftMinutes"] = (int)timeLeft.TotalMinutes,
                        ["renewalThresholdMinutes"] = renewalThresholdMinutes,
                        ["canRenewAt"] = DateTime.UtcNow.AddMinutes(timeLeft.TotalMinutes - renewalThresholdMinutes)
                    });
            }
        }

        // Jeśli odnowienie dozwolone - wydaje nową licencję
        var license = await GetLicenseAsync(contentId, quality, user, ct);

        // Audit log dla renewal
        await _auditService.LogLicenseRenewedAsync(
            user.UserId, contentId, quality, user.Plan, license.ExpiresAt!.Value, ct);

        _logger.LogInformation(
            "License renewed successfully for user {UserId} - content {ContentId} quality {Quality}",
            user.UserId, contentId, quality);

        return license;
    }

    /// <summary>
    /// Pobiera listę dostępnych jakości dla contentu i użytkownika.
    /// Sprawdza uprawnienia do contentu i zwraca tylko jakości dozwolone dla planu użytkownika.
    /// </summary>
    public async Task<List<string>> GetAvailableQualitiesForUserAsync(
        string contentId,
        User user,
        CancellationToken ct = default)
    {
        // Walidacja parametrów
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        // Pobiera metadane contentu (RequiredPlan)
        var contentMeta = await GetContentMetadataAsync(contentId, ct);

        if (contentMeta == null)
        {
            _logger.LogWarning("Content metadata not found for contentId: {ContentId}", contentId);
            throw new NotFoundException(
                $"Content o ID '{contentId}' nie został znaleziony.",
                contentId
            );
        }

        // Sprawdza uprawnienia do contentu (RequiredPlan)
        if (!Plans.HasSufficientPlan(user.Plan, contentMeta.RequiredPlan))
        {
            _logger.LogWarning(
                "Insufficient plan for user {UserId} (plan: {UserPlan}) to access content {ContentId} (required: {RequiredPlan})",
                user.UserId, user.Plan, contentId, contentMeta.RequiredPlan);

            throw new ForbiddenException(
                $"Twój plan ({user.Plan}) nie pozwala na odtworzenie tego contentu. Wymagany plan: {contentMeta.RequiredPlan}",
                new Dictionary<string, object>
                {
                    ["userPlan"] = user.Plan,
                    ["requiredPlan"] = contentMeta.RequiredPlan,
                    ["contentId"] = contentId
                }
            );
        }

        // Pobiera dostępne jakości dla contentu i planu użytkownika
        var availableQualities = await GetAvailableQualitiesAsync(contentId, user.Plan, ct);

        _logger.LogInformation(
            "Available qualities for user {UserId} (plan: {Plan}) on content {ContentId}: {Qualities}",
            user.UserId, user.Plan, contentId, string.Join(", ", availableQualities));

        return availableQualities;
    }

    /// <summary>
    /// Importuje CEK dla contentu (używane przez skrypt importu).
    /// CEK jest walidowany i szyfrowany przed zapisem do Redis.
    /// </summary>
    public async Task ImportCekAsync(
        string contentId,
        string quality,
        string key,
        string keyId,
        CancellationToken ct = default)
    {
        // Walidacja CEK
        if (!CekValidator.Validate(key, out var cekError))
        {
            _logger.LogError("Invalid CEK for {ContentId}:{Quality} - {Error}", contentId, quality, cekError);
            throw new ValidationException($"Invalid CEK: {cekError}");
        }

        // Walidacja KeyId
        if (!CekValidator.ValidateKeyId(keyId, out var keyIdError))
        {
            _logger.LogError("Invalid KeyId for {ContentId}:{Quality} - {Error}", contentId, quality, keyIdError);
            throw new ValidationException($"Invalid KeyId: {keyIdError}");
        }

        // Walidacja Quality
        if (!Qualities.IsValid(quality))
        {
            throw new ValidationException($"Invalid quality: {quality}");
        }

        // Szyfruje CEK za pomocą master key
        string encryptedKey;
        try
        {
            encryptedKey = _cekEncryption.Encrypt(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt CEK for {ContentId}:{Quality}", contentId, quality);
            throw new InternalServerException("Failed to encrypt CEK", ex);
        }

        var encryptedCekData = new EncryptedCekData
        {
            EncryptedKey = encryptedKey,
            KeyId = keyId
        };

        // Zapisuje zaszyfrowany CEK do Redis
        var cekKey = $"{CekKeyPrefix}{contentId}:{quality}";
        var cekJson = JsonSerializer.Serialize(encryptedCekData, _jsonOptions);
        await _redisDb.StringSetAsync(cekKey, cekJson);

        // Dodaje quality do Redis SET (dla szybkiego listingu dostępnych jakości)
        var qualitiesSetKey = $"{AvailableQualitiesSetPrefix}{contentId}";
        await _redisDb.SetAddAsync(qualitiesSetKey, quality);

        _logger.LogInformation("Imported encrypted CEK for content {ContentId} quality {Quality}", contentId, quality);
    }

    /// <summary>
    /// Importuje metadane contentu (RequiredPlan).
    /// </summary>
    public async Task ImportContentMetadataAsync(
        string contentId,
        string requiredPlan,
        CancellationToken ct = default)
    {
        if (!Plans.IsValid(requiredPlan))
        {
            throw new ValidationException($"Nieprawidłowy plan: {requiredPlan}");
        }

        var meta = new ContentMetadataInternal
        {
            RequiredPlan = requiredPlan
        };

        var metaKey = $"{ContentMetaPrefix}{contentId}";
        var metaJson = JsonSerializer.Serialize(meta, _jsonOptions);

        await _redisDb.StringSetAsync(metaKey, metaJson);

        _logger.LogInformation("Imported content metadata for {ContentId}, requiredPlan: {RequiredPlan}",
            contentId, requiredPlan);
    }

    /// <summary>
    /// Sprawdza limit concurrent streams dla użytkownika.
    /// free/basic: max 1 stream, pro: max 2 streamy jednocześnie.
    /// FIXED: Używa Serializable isolation level aby zapobiec race condition.
    /// </summary>
    private async Task CheckConcurrentStreamLimitAsync(
        string userId,
        string contentId,
        string quality,
        string userPlan,
        CancellationToken ct)
    {
        // Pobiera limit dla planu użytkownika
        var limit = _configuration.GetValue<int>($"License:ConcurrentStreamLimits:{userPlan}", 1);

        // Używa Serializable transaction aby zapobiec race condition
        // Bez tego 2 requesty mogły jednocześnie sprawdzić count=0 i oba przejść
        using var transaction = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        try
        {
            // Pobiera wszystkie aktywne licencje użytkownika z bazy danych
            var activeLicenses = await _dbContext.IssuedLicenses
                .Where(l =>
                    l.UserId == userId &&
                    l.ExpiresAt > DateTime.UtcNow &&
                    // Pomija tę samą licencję która teraz odnawiana
                    !(l.ContentId == contentId && l.Quality == quality))
                .ToListAsync(ct);

            var activeStreams = activeLicenses.Count;
            var activeStreamsList = activeLicenses
                .Select(l => $"{l.ContentId}:{l.Quality}")
                .ToList();

            // Sprawdza czy przekroczono limit
            if (activeStreams >= limit)
            {
                _logger.LogWarning(
                    "Concurrent stream limit exceeded for user {UserId} (plan: {Plan}). Active: {Active}/{Limit}. Streams: {Streams}",
                    userId, userPlan, activeStreams, limit, string.Join(", ", activeStreamsList));

                await transaction.RollbackAsync(ct);

                throw new ForbiddenException(
                    $"Osiągnięto limit jednoczesnych streamów dla Twojego planu ({userPlan}): {limit}. " +
                    $"Aktualnie aktywne streamy ({activeStreams}): {string.Join(", ", activeStreamsList)}. " +
                    "Zamknij jeden z aktywnych streamów lub poczekaj na wygaśnięcie licencji.",
                    new Dictionary<string, object>
                    {
                        ["userPlan"] = userPlan,
                        ["limit"] = limit,
                        ["activeStreams"] = activeStreams,
                        ["activeStreamsList"] = activeStreamsList
                    }
                );
            }

            await transaction.CommitAsync(ct);
        }
        catch (ForbiddenException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking concurrent stream limit for user {UserId}", userId);
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Pobiera metadane contentu z Redis.
    /// </summary>
    private async Task<ContentMetadataInternal?> GetContentMetadataAsync(string contentId, CancellationToken ct)
    {
        var metaKey = $"{ContentMetaPrefix}{contentId}";
        var metaJson = await _redisDb.StringGetAsync(metaKey);

        if (!metaJson.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ContentMetadataInternal>(metaJson.ToString(), _jsonOptions);
    }

    /// <summary>
    /// Pobiera listę RZECZYWIŚCIE dostępnych jakości dla contentu,
    /// przefiltrowaną przez plan użytkownika. Wyniki są cache'owane w Redis (TTL: 3600s).
    /// Używa Redis SET (SMEMBERS) O(1) zamiast O(N).
    /// </summary>
    private async Task<List<string>> GetAvailableQualitiesAsync(
        string contentId,
        string userPlan,
        CancellationToken ct)
    {
        // Sprawdza najpierw cache
        var cacheKey = $"content:qualities:{contentId}:{userPlan}";
        var cachedJson = await _redisDb.StringGetAsync(cacheKey);

        if (cachedJson.HasValue)
        {
            var cached = JsonSerializer.Deserialize<List<string>>(cachedJson.ToString(), _jsonOptions);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for qualities: {ContentId}, plan: {Plan}", contentId, userPlan);
                return cached;
            }
        }

        _logger.LogDebug("Cache miss for qualities: {ContentId}, plan: {Plan} - fetching from Redis SET", contentId, userPlan);

        // Pobiera maksymalną jakość dla planu użytkownika
        var maxQuality = Plans.GetMaxQuality(userPlan);

        // Format SET key: content:qualities:{contentId}
        var qualitiesSetKey = $"{AvailableQualitiesSetPrefix}{contentId}";
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
    /// Zapisuje informację o wydanej licencji w PostgreSQL (tracking dla concurrent streams i renewal).
    /// </summary>
    private async Task SaveIssuedLicenseAsync(
        string userId,
        string contentId,
        string quality,
        DateTime expiresAt,
        CancellationToken ct)
    {
        // Sprawdza czy już istnieje aktywna licencja dla tej kombinacji
        var existingLicense = await _dbContext.IssuedLicenses
            .FirstOrDefaultAsync(l =>
                l.UserId == userId &&
                l.ContentId == contentId &&
                l.Quality == quality, ct);

        if (existingLicense != null)
        {
            // Aktualizuje istniejącą licencję
            existingLicense.IssuedAt = DateTime.UtcNow;
            existingLicense.ExpiresAt = expiresAt;
        }
        else
        {
            // Dodaje nową licencję
            var licenseEntity = new IssuedLicenseEntity
            {
                UserId = userId,
                ContentId = contentId,
                Quality = quality,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            _dbContext.IssuedLicenses.Add(licenseEntity);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Model wewnętrzny dla zaszyfrowanego CEK w Redis.
    /// EncryptedKey zawiera: Base64(nonce + tag + ciphertext) zaszyfrowane master key-em.
    /// </summary>
    private class EncryptedCekData
    {
        public string EncryptedKey { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model wewnętrzny dla metadanych contentu.
    /// </summary>
    private class ContentMetadataInternal
    {
        public string RequiredPlan { get; set; } = Plans.FREE;
    }
}
