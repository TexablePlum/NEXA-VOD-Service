using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using Nexa.Shared.Constants;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis zarządzania użytkownikami.
/// Przechowuje użytkowników w PostgreSQL.
/// </summary>
public class UserService
{
    private readonly NexaDbContext _dbContext;
    private readonly ILogger<UserService> _logger;

    public UserService(NexaDbContext dbContext, ILogger<UserService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Tworzy nowego użytkownika.
    /// </summary>
    public async Task<User> CreateUserAsync(string email, string passwordHash, string plan, CancellationToken ct = default)
    {
        // Sprawdza czy użytkownik już istnieje
        var existingUser = await GetUserByEmailAsync(email, ct);
        if (existingUser != null)
        {
            throw new ValidationException(
                "Użytkownik o podanym adresie email już istnieje.",
                new Dictionary<string, object> { ["email"] = email }
            );
        }

        // Waliduje plan
        if (!Plans.IsValid(plan))
        {
            throw new ValidationException(
                $"Nieprawidłowy plan subskrypcji: {plan}",
                new Dictionary<string, object> { ["plan"] = plan }
            );
        }

        var entity = new UserEntity
        {
            UserId = Guid.NewGuid().ToString(),
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            Plan = plan,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Users.Add(entity);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Created new user: {UserId}, email: {Email}, plan: {Plan}",
            entity.UserId, entity.Email, entity.Plan);

        return MapToModel(entity);
    }

    /// <summary>
    /// Pobiera użytkownika po ID.
    /// </summary>
    public async Task<User?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        var entity = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

        return entity == null ? null : MapToModel(entity);
    }

    /// <summary>
    /// Pobiera użytkownika po email.
    /// </summary>
    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.ToLowerInvariant();
        var entity = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        return entity == null ? null : MapToModel(entity);
    }

    /// <summary>
    /// Aktualizuje użytkownika.
    /// </summary>
    public async Task<User> UpdateUserAsync(User user, CancellationToken ct = default)
    {
        var entity = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.UserId == user.UserId, ct);

        if (entity == null)
        {
            throw new NotFoundException(
                $"Użytkownik o ID {user.UserId} nie został znaleziony.",
                user.UserId
            );
        }

        // Aktualizuje właściwości
        entity.Email = user.Email.ToLowerInvariant();
        entity.PasswordHash = user.PasswordHash;
        entity.Plan = user.Plan;
        entity.IsActive = user.IsActive;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated user: {UserId}", user.UserId);

        return MapToModel(entity);
    }

    /// <summary>
    /// Usuwa użytkownika (soft delete - ustawia IsActive = false).
    /// </summary>
    public async Task<bool> DeactivateUserAsync(string userId, CancellationToken ct = default)
    {
        var entity = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

        if (entity == null)
        {
            return false;
        }

        entity.IsActive = false;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Deactivated user: {UserId}", userId);

        return true;
    }

    /// <summary>
    /// Mapuje UserEntity na User model.
    /// </summary>
    private static User MapToModel(UserEntity entity)
    {
        return new User
        {
            UserId = entity.UserId,
            Email = entity.Email,
            PasswordHash = entity.PasswordHash,
            Plan = entity.Plan,
            CreatedAt = entity.CreatedAt,
            IsActive = entity.IsActive
        };
    }
}
