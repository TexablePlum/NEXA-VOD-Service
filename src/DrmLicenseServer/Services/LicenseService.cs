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
    private readonly DeviceKeyService _deviceKeyService;
    private readonly CekPublicKeyEncryptionService _publicKeyEncryption;
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
        CekEncryptionService cekEncryption,
        DeviceKeyService deviceKeyService,
        CekPublicKeyEncryptionService publicKeyEncryption)
    {
        _redisDb = redisDb;
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
        _auditService = auditService;
        _cekEncryption = cekEncryption;
        _deviceKeyService = deviceKeyService;
        _publicKeyEncryption = publicKeyEncryption;
    }

    /// <summary>
    /// Pobiera wszystkie licencje (CEK) dla wszystkich dostępnych jakości contentu w jednym requeście.
    /// Zwraca klucze zaszyfrowane public keyem urządzenia.
    /// CEK szyfrowany public keyem RSA przed wysłaniem do klienta.
    /// </summary>
    public async Task<MultiQualityLicenseResponse> GetAllLicensesAsync(
        string contentId,
        User user,
        string deviceId,
        CancellationToken ct = default)
    {
        // Walidacja parametrów
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ValidationException("Device ID nie może być pusty.");
        }

        // Path traversal protection
        if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
        {
            _logger.LogWarning("Path traversal attempt blocked in multi-license request: {ContentId}", contentId);
            throw new ValidationException("Nieprawidłowy format Content ID.");
        }

        // Pobiera metadane contentu
        var contentMeta = await GetContentMetadataAsync(contentId, ct);

        if (contentMeta == null)
        {
            _logger.LogWarning("Content metadata not found for contentId: {ContentId}", contentId);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, "all",
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
                user.UserId, contentId, "all",
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

        // Pobiera rzeczywiście dostępne jakości dla tego contentu i planu użytkownika
        var availableQualities = await GetAvailableQualitiesAsync(contentId, user.Plan, ct);

        if (availableQualities.Count == 0)
        {
            _logger.LogWarning("No qualities available for content {ContentId} and user plan {Plan}",
                contentId, user.Plan);

            // Audit log
            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, "all",
                $"No qualities available for plan: {user.Plan}",
                ErrorCode.CONTENT_NOT_FOUND,
                ct);

            throw new NotFoundException(
                $"Content '{contentId}' nie ma dostępnych jakości dla Twojego planu ({user.Plan}).",
                contentId
            );
        }

        // Pobiera public key urządzenia (walidacja że urządzenie jest zarejestrowane)
        string publicKeyPem;
        try
        {
            publicKeyPem = await _deviceKeyService.GetDevicePublicKeyAsync(user.UserId, deviceId, ct);
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Device not found for user {UserId}: {DeviceId}", user.UserId, deviceId);
            throw new NotFoundException(
                $"Urządzenie '{deviceId}' nie jest zarejestrowane. Zarejestruj urządzenie na POST /api/device/register",
                deviceId
            );
        }

        // Oblicza czas wygaśnięcia licencji (wspólny dla wszystkich jakości)
        var expirationHours = _configuration.GetValue<int>("License:ExpirationHours", 8);
        var expiresAt = DateTime.UtcNow.AddHours(expirationHours);

        // Pobiera i deszyfruje wszystkie CEK-i (przed rozpoczęciem transakcji DB)
        var decryptedCeks = new List<(string quality, string decryptedKey, string keyId)>();

        foreach (var quality in availableQualities)
        {
            // Pobiera CEK dla tej jakości (zaszyfrowany w Redis)
            var cekKey = $"{CekKeyPrefix}{contentId}:{quality}";
            var cekJson = await _redisDb.StringGetAsync(cekKey);

            if (!cekJson.HasValue)
            {
                _logger.LogWarning("CEK not found for {ContentId} quality {Quality} - skipping this quality", contentId, quality);
                continue;
            }

            var encryptedCekData = JsonSerializer.Deserialize<EncryptedCekData>(cekJson.ToString(), _jsonOptions);

            if (encryptedCekData == null)
            {
                _logger.LogWarning("Failed to deserialize CEK data for {ContentId} quality {Quality} - skipping", contentId, quality);
                continue;
            }

            // Deszyfruje CEK za pomocą master key-a
            string decryptedKey;
            try
            {
                decryptedKey = _cekEncryption.Decrypt(encryptedCekData.EncryptedKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt CEK for {ContentId}:{Quality} - skipping", contentId, quality);
                continue;
            }

            decryptedCeks.Add((quality, decryptedKey, encryptedCekData.KeyId));
            _logger.LogDebug("Decrypted CEK for quality {Quality}", quality);
        }

        if (decryptedCeks.Count == 0)
        {
            _logger.LogError("Failed to retrieve any CEK for content {ContentId}, available qualities: {Qualities}",
                contentId, string.Join(", ", availableQualities));

            _ = _auditService.LogLicenseRejectedAsync(
                user.UserId, contentId, "all",
                "Failed to retrieve any CEK",
                ErrorCode.INTERNAL_SERVER_ERROR,
                ct);

            throw new InternalServerException(
                $"Nie udało się pobrać kluczy szyfrujących dla contentu '{contentId}'. Skontaktuj się z supportem."
            );
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        try
        {
            // Sprawdza limit konkurencyjnych stream-ów w transkacji
            await CheckConcurrentStreamLimitAsync(user.UserId, contentId, string.Empty, user.Plan, ct);

            // Zapisuje informację o wydanych licencjach w transkacji
            foreach (var (quality, _, _) in decryptedCeks)
            {
                await SaveIssuedLicenseAsync(user.UserId, contentId, quality, expiresAt, ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        // Po udanym zapisie szyfruje CEK public keyem urządzenia
        var licenses = new List<QualityLicense>();
        foreach (var (quality, decryptedKey, keyId) in decryptedCeks)
        {
            // Szyfruje CEK public keyem RSA przed wysłaniem
            string encryptedCekForDevice;
            try
            {
                encryptedCekForDevice = _publicKeyEncryption.EncryptCekWithPublicKey(decryptedKey, publicKeyPem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt CEK with device public key for quality {Quality}", quality);
                // W tym momencie licencja jest już zapisana w DB - skip
                continue;
            }

            licenses.Add(new QualityLicense
            {
                Quality = quality,
                EncryptedKey = encryptedCekForDevice, // Zaszyfrowany public keyem RSA
                KeyId = keyId
            });

            _logger.LogDebug("Encrypted CEK for quality {Quality} with device public key", quality);
        }

        _logger.LogInformation(
            "Multi-quality license issued for user {UserId} (plan: {Plan}, device: {DeviceId}) - content {ContentId}, qualities: {Qualities}, expires at {ExpiresAt}",
            user.UserId, user.Plan, deviceId, contentId, string.Join(", ", licenses.Select(l => l.Quality)), expiresAt);

        // Audit log
        await _auditService.LogLicenseIssuedAsync(
            user.UserId, contentId, $"multi({licenses.Count})", user.Plan, expiresAt, ct);

        return new MultiQualityLicenseResponse
        {
            ContentId = contentId,
            UserPlan = user.Plan,
            MaxQuality = Plans.GetMaxQuality(user.Plan),
            ExpiresAt = expiresAt,
            Licenses = licenses
        };
    }

    /// <summary>
    /// Aktualizuje heartbeat dla licencji contentu.
    /// Klient powinien wysyłać heartbeat co 30-60 sekund podczas aktywnego odtwarzania.
    /// Stream jest uznawany za aktywny tylko jeśli ostatni heartbeat był < 2 minuty temu.
    /// </summary>
    public async Task HeartbeatAsync(
        string contentId,
        User user,
        CancellationToken ct = default)
    {
        // Walidacja
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        // Path traversal protection
        if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
        {
            _logger.LogWarning("Path traversal attempt blocked in heartbeat request: {ContentId}", contentId);
            throw new ValidationException("Nieprawidłowy format Content ID.");
        }

        // Aktualizuje LastHeartbeat dla wszystkich jakości tego contentu dla tego usera
        var licenses = await _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == user.UserId &&
                l.ContentId == contentId &&
                l.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        if (licenses.Count == 0)
        {
            _logger.LogWarning(
                "Heartbeat for non-existent license - user {UserId}, content {ContentId}",
                user.UserId, contentId);

            throw new NotFoundException(
                $"Nie znaleziono aktywnej licencji dla contentu '{contentId}'.",
                contentId
            );
        }

        var now = DateTime.UtcNow;
        foreach (var license in licenses)
        {
            license.LastHeartbeat = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Heartbeat updated for user {UserId}, content {ContentId}, qualities: {Qualities}",
            user.UserId, contentId, string.Join(", ", licenses.Select(l => l.Quality)));
    }

    /// <summary>
    /// Usuwa (revoke) licencje dla contentu - zwalnia slot concurrent stream.
    /// Wymaga że user jest właścicielem licencji (sprawdzane po userId).
    /// Używane gdy user zatrzymuje odtwarzanie przed wygaśnięciem licencji.
    /// </summary>
    public async Task RevokeLicenseAsync(
        string contentId,
        User user,
        CancellationToken ct = default)
    {
        // Walidacja
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        // Path traversal protection
        if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
        {
            _logger.LogWarning("Path traversal attempt blocked in revoke request: {ContentId}", contentId);
            throw new ValidationException("Nieprawidłowy format Content ID.");
        }

        // Usuwa licencje dla tego contentu tylko dla tego usera
        var deletedCount = await _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == user.UserId &&
                l.ContentId == contentId)
            .ExecuteDeleteAsync(ct);

        if (deletedCount == 0)
        {
            _logger.LogWarning(
                "Revoke attempt for non-existent license - user {UserId}, content {ContentId}",
                user.UserId, contentId);

            throw new NotFoundException(
                $"Nie znaleziono licencji dla contentu '{contentId}'.",
                contentId
            );
        }

        _logger.LogInformation(
            "License revoked for user {UserId}, content {ContentId}, deleted {Count} license records",
            user.UserId, contentId, deletedCount);

        // Audit log
        await _auditService.LogLicenseRevokedAsync(
            user.UserId, contentId, "manual", user.Plan, ct);
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
    /// Działa w ramach zewnętrznej transakcji Serializable (zapobiega race condition).
    /// Liczy unikalne content ID (jeden content = jeden stream, niezależnie od liczby jakości).
    /// Stream aktywny tylko jeśli LastHeartbeat < 2 minuty temu (heartbeat mechanism).
    /// Ta metoda MUSI być wywoływana w ramach Serializable transaction!
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

        // Heartbeat timeout - stream jest aktywny tylko jeśli ostatni heartbeat < 2 minuty temu
        var heartbeatTimeoutMinutes = _configuration.GetValue<int>("License:HeartbeatTimeoutMinutes", 2);
        var heartbeatCutoff = DateTime.UtcNow.AddMinutes(-heartbeatTimeoutMinutes);

        // Pobiera wszystkie faktycznie aktywne licencje użytkownika z bazy danych
        // Aktywny stream = ExpiresAt w przyszłości I LastHeartbeat świeży (< 2 min temu)
        var activeLicenses = await _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == userId &&
                l.ExpiresAt > DateTime.UtcNow &&
                l.LastHeartbeat > heartbeatCutoff &&
                l.ContentId != contentId)
            .ToListAsync(ct);

        // Liczy unikalne content ID - każdy content to jeden stream niezależnie od liczby jakości
        var uniqueContentIds = activeLicenses
            .Select(l => l.ContentId)
            .Distinct()
            .ToList();

        var activeStreams = uniqueContentIds.Count;

        // Dla logowania - pokazuje wszystkie jakości i czas ostatniego heartbeatu
        var activeStreamsList = activeLicenses
            .GroupBy(l => l.ContentId)
            .Select(g =>
            {
                var lastHeartbeat = g.Max(l => l.LastHeartbeat);
                var secondsAgo = (int)(DateTime.UtcNow - lastHeartbeat).TotalSeconds;
                return $"{g.Key} ({string.Join(", ", g.Select(l => l.Quality))}, heartbeat {secondsAgo}s ago)";
            })
            .ToList();

        // Sprawdza czy przekroczono limit
        if (activeStreams >= limit)
        {
            _logger.LogWarning(
                "Concurrent stream limit exceeded for user {UserId} (plan: {Plan}). Active: {Active}/{Limit}. Streams: {Streams}",
                userId, userPlan, activeStreams, limit, string.Join("; ", activeStreamsList));

            throw new ForbiddenException(
                $"Osiągnięto limit jednoczesnych streamów dla Twojego planu ({userPlan}): {limit}. " +
                $"Aktualnie aktywne streamy ({activeStreams}): {string.Join("; ", activeStreamsList)}. " +
                "Zamknij jeden z aktywnych streamów.",
                new Dictionary<string, object>
                {
                    ["userPlan"] = userPlan,
                    ["limit"] = limit,
                    ["activeStreams"] = activeStreams,
                    ["activeContentIds"] = uniqueContentIds
                }
            );
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

        var now = DateTime.UtcNow;

        if (existingLicense != null)
        {
            // Aktualizuje istniejącą licencję
            existingLicense.IssuedAt = now;
            existingLicense.ExpiresAt = expiresAt;
            existingLicense.LastHeartbeat = now; // Resetuje heartbeat przy odnowieniu
        }
        else
        {
            // Dodaje nową licencję
            var licenseEntity = new IssuedLicenseEntity
            {
                UserId = userId,
                ContentId = contentId,
                Quality = quality,
                IssuedAt = now,
                ExpiresAt = expiresAt,
                LastHeartbeat = now // Ustawia początkowy heartbeat
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
