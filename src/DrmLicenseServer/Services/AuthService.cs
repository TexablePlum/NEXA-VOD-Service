using Nexa.DrmLicenseServer.Services.Auth;
using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis autentykacji użytkowników.
/// Deleguje token generation do TokenService i rate limiting do LoginRateLimiter.
/// </summary>
public class AuthService
{
    private readonly UserService _userService;
    private readonly TokenService _tokenService;
    private readonly LoginRateLimiter _rateLimiter;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserService userService,
        TokenService tokenService,
        LoginRateLimiter rateLimiter,
        ILogger<AuthService> logger)
    {
        _userService = userService;
        _tokenService = tokenService;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Rejestruje nowego użytkownika.
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        // Hash hasła
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        // Tworzy użytkownika
        var user = await _userService.CreateUserAsync(
            request.Email,
            passwordHash,
            request.Plan,
            ct);

        // Generuje tokeny
        return await _tokenService.GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Loguje użytkownika.
    /// Implementuje rate limiting per email.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        // Rate limiting: sprawdza liczbę nieudanych prób logowania
        await _rateLimiter.CheckLimitAsync(request.Email, ct);

        var user = await _userService.GetUserByEmailAsync(request.Email, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            var attemptCount = await _rateLimiter.RecordFailedAttemptAsync(request.Email, ct);
            var maxAttempts = 5; // TODO: pobrać z konfiguracji

            throw new UnauthorizedException(
                "Nieprawidłowy email lub hasło.",
                new Dictionary<string, object>
                {
                    ["email"] = request.Email,
                    ["remainingAttempts"] = Math.Max(0, maxAttempts - attemptCount)
                }
            );
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException(
                "Konto zostało dezaktywowane.",
                new Dictionary<string, object> { ["userId"] = user.UserId }
            );
        }

        // Sukces - resetuje counter
        await _rateLimiter.ResetAttemptsAsync(request.Email, ct);

        _logger.LogInformation("User logged in: {UserId}", user.UserId);

        return await _tokenService.GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Odświeża access token używając refresh tokenu.
    /// </summary>
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        return await _tokenService.RefreshTokenAsync(refreshToken, ct);
    }
}
