using Microsoft.EntityFrameworkCore;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Nexa.Shared.Exceptions;
using Npgsql;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nexa.Shared.Models;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis do zarządzania kluczami publicznymi urządzeń użytkowników.
/// </summary>
public class DeviceKeyService
{
    private readonly NexaDbContext _dbContext;
    private readonly CekPublicKeyEncryptionService _encryptionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeviceKeyService> _logger;
    private readonly IConnectionMultiplexer _redisConnection;

    private const int MaxDevicesPerUser = 10; // Limit urządzeń na użytkownika

    private static readonly Regex DeviceIdPattern = new(@"^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

    public DeviceKeyService(
        NexaDbContext dbContext,
        CekPublicKeyEncryptionService encryptionService,
        IConnectionMultiplexer redisConnection,
        IConfiguration configuration,
        ILogger<DeviceKeyService> logger)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _redisConnection = redisConnection;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Generuje wyzwanie (Nonce) dla klienta.
    /// Zapisuje je w Redis na 2 minuty.
    /// </summary>
    public async Task<DeviceChallengeResponse> GenerateChallengeAsync(string deviceId)
    {
        var nonceBytes = new byte[32];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);

        var db = _redisConnection.GetDatabase();
        await db.StringSetAsync($"device:challenge:{deviceId}", nonce, TimeSpan.FromMinutes(2));

        return new DeviceChallengeResponse { Nonce = nonce };
    }

    /// <summary>
    /// Rejestruje nowe urządzenie z public keyem dla użytkownika.
    /// </summary>
    public async Task<UserDeviceKeyEntity> RegisterDeviceAsync(
        string userId,
        string deviceId,
        string publicKeyPem,
        string nonce,
        string signatureBase64,
        string? deviceName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ValidationException("Device ID cannot be empty");
        }

        if (!DeviceIdPattern.IsMatch(deviceId))
        {
            _logger.LogWarning("Invalid Device ID format rejected: {DeviceId}", deviceId);
            throw new ValidationException(
                "Nieprawidłowy format Device ID. Dozwolone tylko znaki alfanumeryczne, myślnik i podkreślenie (1-64 znaków).");
        }

        // Walidacja public key
        if (!_encryptionService.ValidatePublicKey(publicKeyPem, out var keyError))
        {
            _logger.LogWarning("Invalid public key during device registration for user {UserId}: {Error}", userId, keyError);
            throw new ValidationException($"Invalid public key: {keyError}");
        }

        // Weryfikacja Nonce z Redis
        var db = _redisConnection.GetDatabase();
        var cachedNonce = await db.StringGetAsync($"device:challenge:{deviceId}");
        if (string.IsNullOrEmpty(cachedNonce) || cachedNonce != nonce)
        {
            throw new UnauthorizedException("Invalid or expired challenge nonce", new Dictionary<string, object> { { "reason", "device-challenge-failed" } });
        }

        // Kryptograficzna Weryfikacja Posiadania Klucza (Proof of Possession)
        try
        {
            var rsa = RSA.Create();
            var keyContentBytes = Convert.FromBase64String(
                publicKeyPem.Replace("-----BEGIN PUBLIC KEY-----", "")
                            .Replace("-----END PUBLIC KEY-----", "")
                            .Replace("\n", "")
                            .Replace("\r", ""));
            rsa.ImportSubjectPublicKeyInfo(keyContentBytes, out _);

            var payloadBytes = System.Text.Encoding.UTF8.GetBytes($"{deviceId}|{nonce}");
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            if (!rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                _logger.LogWarning("Invalid RSA signature for device registration. User: {UserId}, Device: {DeviceId}", userId, deviceId);
                throw new UnauthorizedException("Cryptographic Proof of Possession failed", new Dictionary<string, object> { { "reason", "device-signature-failed" } });
            }
        }
        catch (Exception ex) when (ex is not UnauthorizedException)
        {
            _logger.LogError(ex, "Error verifying device key signature. User: {UserId}, Device: {DeviceId}", userId, deviceId);
            throw new ValidationException("Invalid signature or public key format");
        }
        
        // Jednorazowe wyzwanie zostało wykorzystane
        await db.KeyDeleteAsync($"device:challenge:{deviceId}");

        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            UserDeviceKeyEntity? deviceToAdd = null;
            try
            {
                // Sprawdza czy urządzenie już istnieje
                var existingDevice = await _dbContext.UserDeviceKeys
                    .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, ct);

                if (existingDevice != null)
                {
                    // Audit log dla zmiany klucza urządzenia
                    var oldKeyHash = ComputeKeyHash(existingDevice.PublicKeyPem);
                    var newKeyHash = ComputeKeyHash(publicKeyPem);
                    var keyChanged = oldKeyHash != newKeyHash;

                    if (keyChanged)
                    {
                        _logger.LogWarning(
                            "SECURITY AUDIT: Device key changed during re-registration. User: {UserId}, Device: {DeviceId}, OldKeyHash: {OldHash}, NewKeyHash: {NewHash}",
                            userId, deviceId, oldKeyHash, newKeyHash);
                    }

                    // Aktualizuje istniejące urządzenie (re-registration)
                    existingDevice.PublicKeyPem = publicKeyPem;
                    existingDevice.DeviceName = deviceName;
                    existingDevice.TpmAttestation = signatureBase64; // Reused column for backward schema compatibility
                    existingDevice.LastUsedAt = DateTime.UtcNow;
                    existingDevice.IsActive = true;

                    await _dbContext.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "Device re-registered for user {UserId}: {DeviceId} (KeyChanged: {KeyChanged})",
                        userId, deviceId, keyChanged);

                    return existingDevice;
                }

                // Sprawdza limit urządzeń
                var userDeviceCount = await _dbContext.UserDeviceKeys
                    .CountAsync(d => d.UserId == userId && d.IsActive, ct);

                if (userDeviceCount >= MaxDevicesPerUser)
                {
                    throw new ForbiddenException(
                        $"Maximum number of devices ({MaxDevicesPerUser}) reached. Please remove an old device first.",
                        new Dictionary<string, object>
                        {
                            ["maxDevices"] = MaxDevicesPerUser,
                            ["currentDevices"] = userDeviceCount
                        });
                }

                // Tworzy nowe urządzenie
                deviceToAdd = new UserDeviceKeyEntity
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    PublicKeyPem = publicKeyPem,
                    TpmAttestation = signatureBase64, // Reused column for backward schema compatibility
                    RegisteredAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _dbContext.UserDeviceKeys.Add(deviceToAdd);
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "New device registered for user {UserId}: {DeviceId} (name: {DeviceName})",
                    userId, deviceId, deviceName ?? "unnamed");

                return deviceToAdd;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx &&
                                                pgEx.SqlState == "23505" && // Unique violation
                                                attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    "Duplicate device key detected during registration (race condition), retrying. User: {UserId}, Device: {DeviceId}, Attempt: {Attempt}",
                    userId, deviceId, attempt + 1);

                if (deviceToAdd != null)
                {
                    _dbContext.Entry(deviceToAdd).State = EntityState.Detached;
                }

                // Krótkie opóźnienie przed retry
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
            }
        }

        throw new InternalServerException("Failed to register device after multiple attempts due to concurrency issues.");
    }

    /// <summary>
    /// Pobiera public key dla urządzenia użytkownika.
    /// Aktualizuje LastUsedAt.
    /// </summary>
    public async Task<string> GetDevicePublicKeyAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default)
    {
        var device = await _dbContext.UserDeviceKeys
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.DeviceId == deviceId &&
                d.IsActive, ct);

        if (device == null)
        {
            throw new NotFoundException(
                $"Device '{deviceId}' not found or inactive. Please register the device first.",
                deviceId);
        }

        // Aktualizuje LastUsedAt
        device.LastUsedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        return device.PublicKeyPem;
    }

    /// <summary>
    /// Sprawdza czy urządzenie ma TPM attestation.
    /// Urządzenia bez TPM są ograniczone do niższych jakości wideo (max 720p).
    /// </summary>
    public async Task<bool> HasTpmAttestationAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default)
    {
        var device = await _dbContext.UserDeviceKeys
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.DeviceId == deviceId &&
                d.IsActive, ct);

        if (device == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(device.TpmAttestation);
    }

    /// <summary>
    /// Pobiera wszystkie urządzenia użytkownika.
    /// </summary>
    public async Task<List<UserDeviceKeyEntity>> GetUserDevicesAsync(
        string userId,
        CancellationToken ct = default)
    {
        return await _dbContext.UserDeviceKeys
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastUsedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Usuwa (dezaktywuje) urządzenie.
    /// </summary>
    public async Task RemoveDeviceAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default)
    {
        var device = await _dbContext.UserDeviceKeys
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.DeviceId == deviceId, ct);

        if (device == null)
        {
            throw new NotFoundException($"Device '{deviceId}' not found.", deviceId);
        }

        // Soft delete - oznacza jako nieaktywne
        device.IsActive = false;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Device removed for user {UserId}: {DeviceId}",
            userId, deviceId);
    }

    /// <summary>
    /// Oblicza SHA-256 hash klucza publicznego (pierwsze 16 znaków hex) dla audit logging.
    /// Używane do śledzenia zmian kluczy urządzeń.
    /// </summary>
    private static string ComputeKeyHash(string publicKeyPem)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(publicKeyPem));
        return Convert.ToHexString(hashBytes)[..16]; // Pierwsze 16 znaków hex (64 bity)
    }
}
