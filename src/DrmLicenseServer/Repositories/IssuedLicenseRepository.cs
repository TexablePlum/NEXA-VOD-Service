using Microsoft.EntityFrameworkCore;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Nexa.Shared.Exceptions;

namespace Nexa.DrmLicenseServer.Repositories;

/// <summary>
/// Repozytorium do zarządzania wydanymi licencjami w bazie danych.
/// </summary>
public class IssuedLicenseRepository
{
    private readonly NexaDbContext _dbContext;
    private readonly ILogger<IssuedLicenseRepository> _logger;

    public IssuedLicenseRepository(
        NexaDbContext dbContext,
        ILogger<IssuedLicenseRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Zapisuje lub aktualizuje licencję w bazie danych.
    /// </summary>
    public async Task SaveOrUpdateLicenseAsync(
        string userId,
        string contentId,
        string quality,
        DateTime expiresAt,
        string? keyId,
        CancellationToken ct = default)
    {
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
            existingLicense.KeyId = keyId; // Aktualizuje KeyId przy odnowieniu
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
                LastHeartbeat = now,
                KeyId = keyId
            };

            _dbContext.IssuedLicenses.Add(licenseEntity);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Pobiera aktywne licencje użytkownika dla contentu.
    /// </summary>
    public async Task<List<IssuedLicenseEntity>> GetActiveLicensesAsync(
        string userId,
        string contentId,
        CancellationToken ct = default)
    {
        return await _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == userId &&
                l.ContentId == contentId &&
                l.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Pobiera wszystkie aktywne licencje użytkownika (z heartbeatem).
    /// </summary>
    public async Task<List<IssuedLicenseEntity>> GetActiveLicensesWithHeartbeatAsync(
        string userId,
        DateTime heartbeatCutoff,
        string? excludeContentId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == userId &&
                l.ExpiresAt > DateTime.UtcNow &&
                l.LastHeartbeat > heartbeatCutoff);

        if (!string.IsNullOrEmpty(excludeContentId))
        {
            query = query.Where(l => l.ContentId != excludeContentId);
        }

        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Aktualizuje heartbeat dla licencji contentu.
    /// </summary>
    public async Task UpdateHeartbeatAsync(
        string userId,
        string contentId,
        CancellationToken ct = default)
    {
        var licenses = await GetActiveLicensesAsync(userId, contentId, ct);

        if (licenses.Count == 0)
        {
            throw new NotFoundException(
                $"Nie znaleziono aktywnej licencji dla contentu '{contentId}'.",
                contentId);
        }

        var now = DateTime.UtcNow;
        foreach (var license in licenses)
        {
            license.LastHeartbeat = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Heartbeat updated for user {UserId}, content {ContentId}, qualities: {Qualities}",
            userId, contentId, string.Join(", ", licenses.Select(l => l.Quality)));
    }

    /// <summary>
    /// Usuwa (revoke) licencje dla contentu.
    /// </summary>
    public async Task<int> RevokeLicensesAsync(
        string userId,
        string contentId,
        CancellationToken ct = default)
    {
        var deletedCount = await _dbContext.IssuedLicenses
            .Where(l =>
                l.UserId == userId &&
                l.ContentId == contentId)
            .ExecuteDeleteAsync(ct);

        if (deletedCount == 0)
        {
            throw new NotFoundException(
                $"Nie znaleziono licencji dla contentu '{contentId}'.",
                contentId);
        }

        _logger.LogInformation(
            "License revoked for user {UserId}, content {ContentId}, deleted {Count} license records",
            userId, contentId, deletedCount);

        return deletedCount;
    }
}
