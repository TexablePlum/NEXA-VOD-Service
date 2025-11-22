using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nexa.DrmLicenseServer.Data;
using Nexa.DrmLicenseServer.Data.Entities;
using Nexa.Shared.Exceptions;
using Nexa.Shared.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Nexa.DrmLicenseServer.Services.Auth;

/// <summary>
/// Serwis do generowania i walidacji tokenów JWT (access + refresh).
/// </summary>
public class TokenService
{
    private readonly NexaDbContext _dbContext;
    private readonly UserService _userService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    private readonly string _jwtSecret;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _accessTokenExpirationMinutes;
    private readonly int _refreshTokenExpirationDays;

    public TokenService(
        NexaDbContext dbContext,
        UserService userService,
        IConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _dbContext = dbContext;
        _userService = userService;
        _configuration = configuration;
        _logger = logger;

        _jwtSecret = _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT Secret not configured");
        _jwtIssuer = _configuration["Jwt:Issuer"] ?? "NexaDRMServer";
        _jwtAudience = _configuration["Jwt:Audience"] ?? "NexaClient";
        _accessTokenExpirationMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 15);
        _refreshTokenExpirationDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7);

        // Walidacja długości secret
        if (_jwtSecret.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters");
        }
    }

    /// <summary>
    /// Generuje access token i refresh token dla użytkownika.
    /// </summary>
    public async Task<AuthResponse> GenerateAuthResponseAsync(User user, CancellationToken ct = default)
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
    /// Odświeża access token używając refresh tokenu.
    /// </summary>
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        // Crypto-secure losowe opóźnienie aby zmniejszyć signal/noise ratio dla timing attacks
        var randomDelay = RandomNumberGenerator.GetInt32(10, 50); // 10-50ms
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
            var additionalDelay = RandomNumberGenerator.GetInt32(10, 50);
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
    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Hashuje refresh token za pomocą SHA256.
    /// </summary>
    private static string HashRefreshToken(string token)
    {
        using var sha256 = SHA256.Create();
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = sha256.ComputeHash(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }
}
