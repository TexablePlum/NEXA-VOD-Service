using Nexa.Shared.Exceptions;
using StackExchange.Redis;

namespace Nexa.DrmLicenseServer.Services.Auth;

/// <summary>
/// Serwis do rate limiting prób logowania per email.
/// Zapobiega brute-force attacks na hasła użytkowników.
/// </summary>
public class LoginRateLimiter
{
    private readonly IDatabase _redisDb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginRateLimiter> _logger;

    public LoginRateLimiter(
        IDatabase redisDb,
        IConfiguration configuration,
        ILogger<LoginRateLimiter> logger)
    {
        _redisDb = redisDb;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Sprawdza czy email jest zablokowany z powodu zbyt wielu nieudanych prób logowania.
    /// </summary>
    public async Task CheckLimitAsync(string email, CancellationToken ct = default)
    {
        var attemptKey = $"login:attempts:{email.ToLowerInvariant()}";
        var attempts = await _redisDb.StringGetAsync(attemptKey);
        var attemptCount = attempts.HasValue ? (int)attempts : 0;

        var maxAttempts = _configuration.GetValue<int>("Auth:MaxLoginAttempts", 5);
        var lockoutMinutes = _configuration.GetValue<int>("Auth:LoginLockoutMinutes", 15);

        if (attemptCount >= maxAttempts)
        {
            var ttl = await _redisDb.KeyTimeToLiveAsync(attemptKey);
            var lockoutRemaining = ttl.HasValue ? (int)ttl.Value.TotalMinutes : lockoutMinutes;

            _logger.LogWarning(
                "Login rate limit exceeded for email: {Email}. Attempts: {Attempts}/{Max}",
                email, attemptCount, maxAttempts);

            throw new ForbiddenException(
                $"Zbyt wiele nieudanych prób logowania. Konto zablokowane na {lockoutRemaining} minut.",
                new Dictionary<string, object>
                {
                    ["email"] = email,
                    ["attempts"] = attemptCount,
                    ["maxAttempts"] = maxAttempts,
                    ["lockoutRemainingMinutes"] = lockoutRemaining
                });
        }
    }

    /// <summary>
    /// Rejestruje nieudaną próbę logowania.
    /// </summary>
    public async Task<int> RecordFailedAttemptAsync(string email, CancellationToken ct = default)
    {
        var attemptKey = $"login:attempts:{email.ToLowerInvariant()}";
        var lockoutMinutes = _configuration.GetValue<int>("Auth:LoginLockoutMinutes", 15);

        var newAttemptCount = await _redisDb.StringIncrementAsync(attemptKey);
        if (newAttemptCount == 1)
        {
            // Gdy pierwsza nieudana próba to ustawia TTL
            await _redisDb.KeyExpireAsync(attemptKey, TimeSpan.FromMinutes(lockoutMinutes));
        }

        _logger.LogWarning(
            "Failed login attempt for email: {Email}. Attempt {Attempt}",
            email, newAttemptCount);

        return (int)newAttemptCount;
    }

    /// <summary>
    /// Resetuje licznik nieudanych prób (po udanym logowaniu).
    /// </summary>
    public async Task ResetAttemptsAsync(string email, CancellationToken ct = default)
    {
        var attemptKey = $"login:attempts:{email.ToLowerInvariant()}";
        await _redisDb.KeyDeleteAsync(attemptKey);
    }
}
