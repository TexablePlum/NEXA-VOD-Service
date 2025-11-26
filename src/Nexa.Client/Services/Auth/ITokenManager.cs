using System;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Interfejs zarządzania tokenami autoryzacji.
/// AccessToken jest przechowywany w pamięci RAM, RefreshToken w bezpiecznym storage (ProtectedData).
/// </summary>
public interface ITokenManager
{
    /// <summary>
    /// Event wywoływany gdy AccessToken wygasł lub wymaga odświeżenia.
    /// </summary>
    event EventHandler? AccessTokenExpired;

    /// <summary>
    /// Przechowuje tokeny w bezpieczny sposób.
    /// </summary>
    /// <param name="accessToken">JWT Access Token</param>
    /// <param name="refreshToken">Refresh Token</param>
    /// <param name="expiresInSeconds">Czas życia Access Token w sekundach</param>
    void StoreTokens(string accessToken, string refreshToken, int expiresInSeconds);

    /// <summary>
    /// Pobiera aktualny Access Token jeśli jest ważny.
    /// </summary>
    /// <returns>Access Token lub null jeśli wygasł</returns>
    string? GetAccessToken();

    /// <summary>
    /// Pobiera Refresh Token z bezpiecznego storage.
    /// </summary>
    /// <returns>Refresh Token lub null jeśli nie istnieje</returns>
    string? GetRefreshToken();

    /// <summary>
    /// Sprawdza czy Access Token jest nadal ważny.
    /// </summary>
    /// <param name="bufferSeconds">Margines bezpieczeństwa w sekundach (domyślnie 60s)</param>
    /// <returns>True jeśli token jest ważny</returns>
    bool IsAccessTokenValid(int bufferSeconds = 60);

    /// <summary>
    /// Sprawdza czy użytkownik jest zalogowany (posiada tokeny).
    /// </summary>
    bool IsAuthenticated();

    /// <summary>
    /// Czyści wszystkie tokeny (wylogowanie).
    /// </summary>
    void ClearTokens();

    /// <summary>
    /// Pobiera czas wygaśnięcia Access Token.
    /// </summary>
    DateTime? GetAccessTokenExpiry();
}
