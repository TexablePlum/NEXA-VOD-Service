namespace Nexa.Shared.Models;

/// <summary>
/// Odpowiedź po udanym logowaniu/rejestracji.
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// Access token JWT (krótki TTL).
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Refresh token (długi TTL).
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Czas wygaśnięcia access tokenu w sekundach.
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Typ tokenu (zawsze "Bearer").
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Informacje o użytkowniku.
    /// </summary>
    public UserInfo User { get; set; } = new();
}

/// <summary>
/// Podstawowe informacje o użytkowniku.
/// </summary>
public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
