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
        ILogger<AuthService> logger,
        IConfiguration configuration)
    {
        _userService = userService;
        _dbContext = dbContext;
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
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _userService.GetUserByEmailAsync(request.Email, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for email: {Email}", request.Email);
            throw new UnauthorizedException(
                "Nieprawidłowy email lub hasło.",
                new Dictionary<string, object> { ["email"] = request.Email }
            );
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException(
                "Konto zostało dezaktywowane.",
                new Dictionary<string, object> { ["userId"] = user.UserId }
            );
        }

        _logger.LogInformation("User logged in: {UserId}", user.UserId);

        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Odświeża access token używając refresh tokenu.
    /// </summary>
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        // Sprawdza czy refresh token istnieje w bazie danych
        var tokenEntity = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken, ct);

        if (tokenEntity == null || tokenEntity.IsRevoked || tokenEntity.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("Invalid, revoked or expired refresh token attempted");
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

        // Oznacza stary refresh token jako revoked
        tokenEntity.IsRevoked = true;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh token used for user: {UserId}", user.UserId);

        // Generuje nowe tokeny
        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Generuje access token i refresh token dla użytkownika.
    /// </summary>
    private async Task<AuthResponse> GenerateAuthResponseAsync(User user, CancellationToken ct)
    {
        // Generuje access token (JWT)
        var accessToken = GenerateAccessToken(user);

        // Generuje refresh token (random)
        var refreshToken = GenerateRefreshToken();

        // Zapisuje refresh token w bazie danych
        var tokenEntity = new RefreshTokenEntity
        {
            Token = refreshToken,
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
            RefreshToken = refreshToken,
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
}
