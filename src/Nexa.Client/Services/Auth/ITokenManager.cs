using System;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Interfejs zarządzania tokenami autoryzacji.
/// AccessToken jest przechowywany w pamięci RAM.
/// RefreshToken może być w pamięci RAM lub Windows Credential Manager (opcja "Remember Me").
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
    /// <param name="email">Email użytkownika (do identyfikacji w Credential Manager)</param>
    /// <param name="persistRefreshToken">Czy zapisać RefreshToken w Windows Credential Manager (Remember Me)</param>
    void StoreTokens(string accessToken, string refreshToken, int expiresInSeconds, string email, bool persistRefreshToken = false);

    /// <summary>
    /// Pobiera aktualny Access Token jeśli jest ważny.
    /// </summary>
    /// <returns>Access Token lub null jeśli wygasł</returns>
    string? GetAccessToken();

    /// <summary>
    /// Pobiera Refresh Token z pamięci RAM lub Credential Manager.
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
    /// Czyści wszystkie tokeny (wylogowanie) - z RAM i Credential Manager.
    /// </summary>
    void ClearTokens();

    /// <summary>
    /// Pobiera czas wygaśnięcia Access Token.
    /// </summary>
    DateTime? GetAccessTokenExpiry();

    /// <summary>
    /// Sprawdza czy istnieje zapisany RefreshToken w Windows Credential Manager.
    /// </summary>
    /// <param name="email">Email użytkownika jeśli znaleziono credential</param>
    /// <returns>True jeśli istnieje zapisany token</returns>
    bool HasSavedRefreshToken(out string? email);

    /// <summary>
    /// Usuwa zapisany RefreshToken z Windows Credential Manager.
    /// </summary>
    /// <param name="email">Email użytkownika którego token ma być usunięty</param>
    void RemoveSavedRefreshToken(string email);

    /// <summary>
    /// Pobiera email aktualnie zalogowanego użytkownika.
    /// </summary>
    string? GetCurrentEmail();

    /// <summary>
    /// Sprawdza czy ostatnie StoreTokens użyło persistRefreshToken = true.
    /// </summary>
    bool IsPersisted();
}
