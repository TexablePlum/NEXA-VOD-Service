using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using Nexa.Shared.Constants;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Services.License;
using Nexa.DrmLicenseServer.Repositories;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis zarządzania licencjami DRM (CEK).
/// Zwraca klucze deszyfrujące dla uprawnionych użytkowników.
/// Używa delegacji do wyspecjalizowanych serwisów.
/// </summary>
public class LicenseService
{
    private readonly NexaDbContext _dbContext;
    private readonly ILogger<LicenseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AuditService _auditService;
    private readonly DeviceKeyService _deviceKeyService;

    // Wyspecjalizowane serwisy
    private readonly LicenseValidationService _validationService;
    private readonly ContentMetadataService _metadataService;
    private readonly QualityService _qualityService;
    private readonly CekManager _cekManager;
    private readonly IssuedLicenseRepository _licenseRepository;
    private readonly ConcurrentStreamManager _streamManager;

    public LicenseService(
        NexaDbContext dbContext,
        ILogger<LicenseService> logger,
        IConfiguration configuration,
        AuditService auditService,
        DeviceKeyService deviceKeyService,
        LicenseValidationService validationService,
        ContentMetadataService metadataService,
        QualityService qualityService,
        CekManager cekManager,
        IssuedLicenseRepository licenseRepository,
        ConcurrentStreamManager streamManager)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
        _auditService = auditService;
        _deviceKeyService = deviceKeyService;
        _validationService = validationService;
        _metadataService = metadataService;
        _qualityService = qualityService;
        _cekManager = cekManager;
        _licenseRepository = licenseRepository;
        _streamManager = streamManager;
    }

    /// <summary>
    /// Pobiera wszystkie licencje (CEK) dla wszystkich dostępnych jakości contentu w jednym requeście.
    /// Zwraca klucze zaszyfrowane public keyem urządzenia.
    /// </summary>
    public async Task<MultiQualityLicenseResponse> GetAllLicensesAsync(
        string contentId,
        User user,
        string deviceId,
        CancellationToken ct = default)
    {
        // 1. Walidacja parametrów
        _validationService.ValidateContentId(contentId);
        _validationService.ValidateDeviceId(deviceId);

        // 2. Pobiera metadane contentu
        var contentMeta = await _metadataService.GetMetadataAsync(contentId, ct);

        if (contentMeta == null)
        {
            _logger.LogWarning("Content metadata not found for contentId: {ContentId}", contentId);
            await LogAuditRejectionAsync(user.UserId, contentId, "all", "Content not found", ErrorCode.CONTENT_NOT_FOUND, ct);

            throw new NotFoundException(
                $"Content o ID '{contentId}' nie został znaleziony.",
                contentId
            );
        }

        // 3. Sprawdza release date - czy content został już wypuszczony
        if (contentMeta.ReleaseDate.HasValue && contentMeta.ReleaseDate.Value > DateTime.UtcNow)
        {
            var releaseDate = contentMeta.ReleaseDate.Value;
            _logger.LogWarning(
                "Content {ContentId} not yet released. Release date: {ReleaseDate}, current time: {CurrentTime}",
                contentId, releaseDate, DateTime.UtcNow);

            await LogAuditRejectionAsync(user.UserId, contentId, "all",
                $"Content not released yet. Release date: {releaseDate:yyyy-MM-dd HH:mm} UTC", ErrorCode.CONTENT_NOT_RELEASED, ct);

            throw new ForbiddenException(
                $"Ten content nie został jeszcze wypuszczony. Premiera: {releaseDate:yyyy-MM-dd HH:mm} UTC",
                new Dictionary<string, object>
                {
                    ["contentId"] = contentId,
                    ["releaseDate"] = releaseDate,
                    ["currentTime"] = DateTime.UtcNow
                }
            );
        }

        // 4. Sprawdza uprawnienia do contentu
        if (!Plans.HasSufficientPlan(user.Plan, contentMeta.RequiredPlan))
        {
            _logger.LogWarning(
                "Insufficient plan for user {UserId} (plan: {UserPlan}) to access content {ContentId} (required: {RequiredPlan})",
                user.UserId, user.Plan, contentId, contentMeta.RequiredPlan);

            await LogAuditRejectionAsync(user.UserId, contentId, "all",
                $"Insufficient plan: {user.Plan} < {contentMeta.RequiredPlan}", ErrorCode.FORBIDDEN, ct);

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

        // 4. Pobiera public key urządzenia i sprawdza TPM
        string publicKeyPem;
        bool hasTpm;
        try
        {
            publicKeyPem = await _deviceKeyService.GetDevicePublicKeyAsync(user.UserId, deviceId, ct);
            hasTpm = await _deviceKeyService.HasTpmAttestationAsync(user.UserId, deviceId, ct);
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Device not found for user {UserId}: {DeviceId}", user.UserId, deviceId);
            throw new NotFoundException(
                $"Urządzenie '{deviceId}' nie jest zarejestrowane. Zarejestruj urządzenie na POST /api/device/register",
                deviceId
            );
        }

        if (!hasTpm)
        {
            _logger.LogInformation(
                "Device {DeviceId} for user {UserId} has no TPM attestation - limiting max quality to 720p",
                deviceId, user.UserId);
        }

        // 5. Pobiera dostępne jakości dla contentu, planu użytkownika i TPM
        var availableQualities = await _qualityService.GetAvailableQualitiesAsync(contentId, user.Plan, hasTpm, ct);

        if (availableQualities.Count == 0)
        {
            _logger.LogWarning("No qualities available for content {ContentId} and user plan {Plan}",
                contentId, user.Plan);

            await LogAuditRejectionAsync(user.UserId, contentId, "all",
                $"No qualities available for plan: {user.Plan}", ErrorCode.CONTENT_NOT_FOUND, ct);

            throw new NotFoundException(
                $"Content '{contentId}' nie ma dostępnych jakości dla Twojego planu ({user.Plan}).",
                contentId
            );
        }

        // 6. Oblicza czas wygaśnięcia licencji
        var expirationHours = _configuration.GetValue<int>("License:ExpirationHours", 8);
        var expiresAt = DateTime.UtcNow.AddHours(expirationHours);

        // 7. Pobiera i deszyfruje wszystkie CEK-i (przed transakcją DB)
        var decryptedCeks = await _cekManager.GetDecryptedCeksAsync(contentId, availableQualities, ct);

        if (decryptedCeks.Count == 0)
        {
            _logger.LogError("Failed to retrieve any CEK for content {ContentId}, available qualities: {Qualities}",
                contentId, string.Join(", ", availableQualities));

            await LogAuditRejectionAsync(user.UserId, contentId, "all",
                "Failed to retrieve any CEK", ErrorCode.INTERNAL_SERVER_ERROR, ct);

            throw new InternalServerException(
                $"Nie udało się pobrać kluczy szyfrujących dla contentu '{contentId}'. Skontaktuj się z supportem."
            );
        }

        // 8. Transakcja DB - sprawdza limity i zapisuje licencje
        using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Sprawdza limit konkurencyjnych stream-ów
            await _streamManager.CheckLimitAsync(user.UserId, contentId, user.Plan, ct);

            // Zapisuje informację o wydanych licencjach
            foreach (var cek in decryptedCeks)
            {
                await _licenseRepository.SaveOrUpdateLicenseAsync(
                    user.UserId, contentId, cek.Quality, expiresAt, cek.KeyId, ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        // 9. Szyfruje CEK public keyem urządzenia
        var licenses = new List<QualityLicense>();
        foreach (var cek in decryptedCeks)
        {
            try
            {
                var encryptedCekForDevice = _cekManager.EncryptCekWithDeviceKey(cek.DecryptedKey, publicKeyPem);

                licenses.Add(new QualityLicense
                {
                    Quality = cek.Quality,
                    EncryptedKey = encryptedCekForDevice,
                    KeyId = cek.KeyId
                });

                _logger.LogDebug("Encrypted CEK for quality {Quality} with device public key", cek.Quality);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt CEK with device public key for quality {Quality}", cek.Quality);
                // Licencja już zapisana w DB - skip
                continue;
            }
        }

        _logger.LogInformation(
            "Multi-quality license issued for user {UserId} (plan: {Plan}, device: {DeviceId}) - content {ContentId}, qualities: {Qualities}, expires at {ExpiresAt}",
            user.UserId, user.Plan, deviceId, contentId, string.Join(", ", licenses.Select(l => l.Quality)), expiresAt);

        // 10. Audit log
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
    /// </summary>
    public async Task HeartbeatAsync(
        string contentId,
        User user,
        CancellationToken ct = default)
    {
        _validationService.ValidateContentIdWithPathTraversal(contentId);
        await _licenseRepository.UpdateHeartbeatAsync(user.UserId, contentId, ct);
    }

    /// <summary>
    /// Usuwa (revoke) licencje dla contentu - zwalnia slot concurrent stream.
    /// </summary>
    public async Task RevokeLicenseAsync(
        string contentId,
        User user,
        CancellationToken ct = default)
    {
        _validationService.ValidateContentIdWithPathTraversal(contentId);

        var deletedCount = await _licenseRepository.RevokeLicensesAsync(user.UserId, contentId, ct);

        // Audit log
        await _auditService.LogLicenseRevokedAsync(
            user.UserId, contentId, "manual", user.Plan, ct);
    }

    /// <summary>
    /// Importuje CEK dla contentu (używane przez skrypt importu).
    /// </summary>
    public async Task ImportCekAsync(
        string contentId,
        string quality,
        string key,
        string keyId,
        CancellationToken ct = default)
    {
        await _cekManager.ImportCekAsync(contentId, quality, key, keyId, ct);
    }

    /// <summary>
    /// Importuje metadane contentu (RequiredPlan i ReleaseDate).
    /// </summary>
    public async Task ImportContentMetadataAsync(
        string contentId,
        string requiredPlan,
        DateTime? releaseDate = null,
        CancellationToken ct = default)
    {
        await _metadataService.ImportMetadataAsync(contentId, requiredPlan, releaseDate, ct);
    }

    /// <summary>
    /// Helper do logowania audytu z obsługą błędów.
    /// </summary>
    private async Task LogAuditRejectionAsync(
        string userId, string contentId, string quality, string reason, string errorCode, CancellationToken ct)
    {
        try
        {
            await _auditService.LogLicenseRejectedAsync(userId, contentId, quality, reason, errorCode, ct);
        }
        catch (Exception auditEx)
        {
            _logger.LogError(auditEx, "Failed to write audit log for license rejection");
        }
    }
}
