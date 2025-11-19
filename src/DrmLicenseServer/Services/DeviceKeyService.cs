using Microsoft.EntityFrameworkCore;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Nexa.Shared.Exceptions;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis do zarządzania kluczami publicznymi urządzeń użytkowników.
/// </summary>
public class DeviceKeyService
{
    private readonly NexaDbContext _dbContext;
    private readonly CekPublicKeyEncryptionService _encryptionService;
    private readonly ILogger<DeviceKeyService> _logger;
    private readonly IConfiguration _configuration;

    private const int MaxDevicesPerUser = 10; // Limit urządzeń na użytkownika

    public DeviceKeyService(
        NexaDbContext dbContext,
        CekPublicKeyEncryptionService encryptionService,
        ILogger<DeviceKeyService> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Rejestruje nowe urządzenie z public keyem dla użytkownika.
    /// </summary>
    public async Task<UserDeviceKeyEntity> RegisterDeviceAsync(
        string userId,
        string deviceId,
        string publicKeyPem,
        string? deviceName = null,
        string? tpmAttestation = null,
        CancellationToken ct = default)
    {
        // Walidacja device ID
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64)
        {
            throw new ValidationException("Device ID must be 1-64 characters");
        }

        // Walidacja public key
        if (!_encryptionService.ValidatePublicKey(publicKeyPem, out var keyError))
        {
            _logger.LogWarning("Invalid public key during device registration for user {UserId}: {Error}", userId, keyError);
            throw new ValidationException($"Invalid public key: {keyError}");
        }

        // Sprawdza czy urządzenie już istnieje
        var existingDevice = await _dbContext.UserDeviceKeys
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, ct);

        if (existingDevice != null)
        {
            // Aktualizuje istniejące urządzenie (re-registration)
            existingDevice.PublicKeyPem = publicKeyPem;
            existingDevice.DeviceName = deviceName;
            existingDevice.TpmAttestation = tpmAttestation;
            existingDevice.LastUsedAt = DateTime.UtcNow;
            existingDevice.IsActive = true;

            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Device re-registered for user {UserId}: {DeviceId}",
                userId, deviceId);

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
        var device = new UserDeviceKeyEntity
        {
            UserId = userId,
            DeviceId = deviceId,
            DeviceName = deviceName,
            PublicKeyPem = publicKeyPem,
            TpmAttestation = tpmAttestation,
            RegisteredAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.UserDeviceKeys.Add(device);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New device registered for user {UserId}: {DeviceId} (name: {DeviceName})",
            userId, deviceId, deviceName ?? "unnamed");

        return device;
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
}
