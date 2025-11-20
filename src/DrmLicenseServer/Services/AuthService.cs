using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis autentykacji JWT.
/// Generuje access tokeny i refresh tokeny, waliduje credentials.
/// Refresh tokeny przechowywane w PostgreSQL.
/// </summary>
public class AuthService
{
    private readonly UserService _userService;
    private readonly NexaDbContext _dbContext;
    private readonly IDatabase _redisDb;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;

    private readonly string _jwtSecret;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _accessTokenExpirationMinutes;
    private readonly int _refreshTokenExpirationDays;

    public AuthService(
        UserService userService,
        NexaDbContext dbContext,
        IDatabase redisDb,
        ILogger<AuthService> logger,
        IConfiguration configuration)
    {
        _userService = userService;
        _dbContext = dbContext;
        _redisDb = redisDb;
        _logger = logger;
        _configuration = configuration;

        _jwtSecret = _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT Secret not configured");
        _jwtIssuer = _configuration["Jwt:Issuer"] ?? "NexaDRMServer";
        _jwtAudience = _configuration["Jwt:Audience"] ?? "NexaClient";
        _accessTokenExpirationMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 15);
        _refreshTokenExpirationDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7);

        // Walidacja długości secret (min 32 znaki)
        if (_jwtSecret.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters");
        }
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
        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Loguje użytkownika.
    /// Implementuje rate limiting per email.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        // Rate limiting: sprawdza liczbę nieudanych prób logowania
        var attemptKey = $"login:attempts:{request.Email.ToLowerInvariant()}";
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
                request.Email, attemptCount, maxAttempts);

            throw new ForbiddenException(
                $"Zbyt wiele nieudanych prób logowania. Konto zablokowane na {lockoutRemaining} minut.",
                new Dictionary<string, object>
                {
                    ["email"] = request.Email,
                    ["attempts"] = attemptCount,
                    ["maxAttempts"] = maxAttempts,
                    ["lockoutRemainingMinutes"] = lockoutRemaining
                });
        }

        var user = await _userService.GetUserByEmailAsync(request.Email, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            var newAttemptCount = await _redisDb.StringIncrementAsync(attemptKey);
            if (newAttemptCount == 1)
            {
                // Gdy pierwsza nieudana próba to ustawia TTL
                await _redisDb.KeyExpireAsync(attemptKey, TimeSpan.FromMinutes(lockoutMinutes));
            }

            _logger.LogWarning(
                "Failed login attempt for email: {Email}. Attempt {Attempt}/{Max}",
                request.Email, newAttemptCount, maxAttempts);

            throw new UnauthorizedException(
                "Nieprawidłowy email lub hasło.",
                new Dictionary<string, object>
                {
                    ["email"] = request.Email,
                    ["remainingAttempts"] = Math.Max(0, maxAttempts - (int)newAttemptCount)
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
        await _redisDb.KeyDeleteAsync(attemptKey);

        _logger.LogInformation("User logged in: {UserId}", user.UserId);

        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Odświeża access token używając refresh tokenu.
    /// Hashuje podany token i porównuje z hashem w bazie.
    /// </summary>
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        // Crypto-secure losowe opóźnienie aby zmniejszyć signal/noise ratio dla timing attacks
        var randomDelay = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10, 50); // 10-50ms
        await Task.Delay(randomDelay, ct);

        // Hash podanego tokenu żeby porównać z hashem w bazie
        var tokenHash = HashRefreshToken(refreshToken);

        // Sprawdza czy zahashowany refresh token istnieje w bazie danych
        var tokenEntity = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash, ct);

        if (tokenEntity == null || tokenEntity.IsRevoked || tokenEntity.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("Invalid, revoked or expired refresh token attempted");

            // Crypto-secure losowe opóźnienie dla invalid token
            var additionalDelay = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10, 50);
            await Task.Delay(additionalDelay, ct);

            throw new UnauthorizedException(
                "Nieprawidłowy lub wygasły refresh token.",
                new Dictionary<string, object> { ["errorCode"] = ErrorCode.INVALID_REFRESH_TOKEN }
            );
        }

        var user = await _userService.GetUserByIdAsync(tokenEntity.UserId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        // Oznacza stary refresh token jako wygasły
        tokenEntity.IsRevoked = true;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh token used for user: {UserId}", user.UserId);

        // Generuje nowe tokeny
        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Generuje access token i refresh token dla użytkownika.
    /// Refresh token jest hashowany SHA256 przed zapisem do DB.
    /// </summary>
    private async Task<AuthResponse> GenerateAuthResponseAsync(User user, CancellationToken ct)
    {
        // Generuje access token (JWT)
        var accessToken = GenerateAccessToken(user);

        // Generuje refresh token (random 256-bit)
        var refreshToken = GenerateRefreshToken();

        // Hash refresh token przed zapisem do bazy (SHA256)
        var tokenHash = HashRefreshToken(refreshToken);

        // Zapisuje zahashowany refresh token w bazie danych
        var tokenEntity = new RefreshTokenEntity
        {
            Token = tokenHash,
            UserId = user.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        _dbContext.RefreshTokens.Add(tokenEntity);
        await _dbContext.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken, // Zwraca plaintext do klienta
            ExpiresIn = _accessTokenExpirationMinutes * 60,
            TokenType = "Bearer",
            User = new UserInfo
            {
                UserId = user.UserId,
                Email = user.Email,
                Plan = user.Plan,
                CreatedAt = user.CreatedAt
            }
        };
    }

    /// <summary>
    /// Generuje JWT access token.
    /// </summary>
    private string GenerateAccessToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("plan", user.Plan),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generuje losowy refresh token (256-bit).
    /// </summary>
    private string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Hashuje refresh token za pomocą SHA256.
    /// Używa SHA256 zamiast BCrypt.
    /// SHA256 jest bezpieczny dla 256-bit random data (brak słownikowych ataków).
    /// </summary>
    private string HashRefreshToken(string token)
    {
        using var sha256 = SHA256.Create();
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = sha256.ComputeHash(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }
}
