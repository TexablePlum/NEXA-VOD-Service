using Nexa.DrmLicenseServer.Services;
using Nexa.DrmLicenseServer.Validation;
using Nexa.Shared.Constants;
using Nexa.Shared.Exceptions;
using StackExchange.Redis;
using System.Text.Json;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do zarządzania kluczami CEK (Content Encryption Key).
/// Odpowiada za import, pobieranie, szyfrowanie i deszyfrowanie CEK.
/// </summary>
public class CekManager
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<CekManager> _logger;
    private readonly CekEncryptionService _cekEncryption;
    private readonly CekPublicKeyEncryptionService _publicKeyEncryption;
    private readonly QualityService _qualityService;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string CekKeyPrefix = "cek:";

    public CekManager(
        IDatabase redisDb,
        ILogger<CekManager> logger,
        CekEncryptionService cekEncryption,
        CekPublicKeyEncryptionService publicKeyEncryption,
        QualityService qualityService)
    {
        _redisDb = redisDb;
        _logger = logger;
        _cekEncryption = cekEncryption;
        _publicKeyEncryption = publicKeyEncryption;
        _qualityService = qualityService;
    }

    /// <summary>
    /// Pobiera i deszyfruje CEK-i dla podanych jakości.
    /// Zwraca listę (quality, decryptedKey, keyId).
    /// </summary>
    public async Task<List<DecryptedCek>> GetDecryptedCeksAsync(
        string contentId,
        List<string> qualities,
        CancellationToken ct = default)
    {
        var decryptedCeks = new List<DecryptedCek>();

        foreach (var quality in qualities)
        {
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

            decryptedCeks.Add(new DecryptedCek(quality, decryptedKey, encryptedCekData.KeyId));
            _logger.LogDebug("Decrypted CEK for quality {Quality}", quality);
        }

        return decryptedCeks;
    }

    /// <summary>
    /// Szyfruje CEK za pomocą public key urządzenia.
    /// </summary>
    public string EncryptCekWithDeviceKey(string decryptedCek, string publicKeyPem)
    {
        return _publicKeyEncryption.EncryptCekWithPublicKey(decryptedCek, publicKeyPem);
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
        await _qualityService.AddQualityAsync(contentId, quality, ct);

        _logger.LogInformation("Imported encrypted CEK for content {ContentId} quality {Quality}", contentId, quality);
    }

    /// <summary>
    /// Model zaszyfrowanego CEK w Redis.
    /// </summary>
    private class EncryptedCekData
    {
        public string EncryptedKey { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model odszyfrowanego CEK.
    /// </summary>
    public record DecryptedCek(string Quality, string DecryptedKey, string KeyId);
}
