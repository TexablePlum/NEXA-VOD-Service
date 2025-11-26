using Nexa.Shared.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Interfejs serwisu autoryzacji.
/// Obsługuje logowanie, rejestrację i odświeżanie tokenów.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Loguje użytkownika i przechowuje tokeny.
    /// </summary>
    /// <param name="email">Email użytkownika</param>
    /// <param name="password">Hasło</param>
    /// <param name="rememberMe">Czy zapisać RefreshToken w Windows Credential Manager (Remember Me)</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>AuthResponse z danymi użytkownika i tokenami</returns>
    Task<AuthResponse> LoginAsync(string email, string password, bool rememberMe = true, CancellationToken ct = default);

    /// <summary>
    /// Rejestruje nowego użytkownika i przechowuje tokeny.
    /// </summary>
    /// <param name="email">Email użytkownika</param>
    /// <param name="password">Hasło</param>
    /// <param name="plan">Plan subskrypcji (domyślnie "free")</param>
    /// <param name="rememberMe">Czy zapisać RefreshToken w Windows Credential Manager (Remember Me)</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>AuthResponse z danymi użytkownika i tokenami</returns>
    Task<AuthResponse> RegisterAsync(string email, string password, string plan = "free", bool rememberMe = true, CancellationToken ct = default);

    /// <summary>
    /// Odświeża wygasły access token używając refresh tokenu.
    /// Automatycznie aktualizuje przechowywane tokeny.
    /// </summary>
    /// <param name="ct">CancellationToken</param>
    /// <returns>AuthResponse z nowymi tokenami</returns>
    Task<AuthResponse> RefreshTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Wylogowuje użytkownika (czyści tokeny lokalnie).
    /// </summary>
    void Logout();

    /// <summary>
    /// Sprawdza czy użytkownik jest zalogowany.
    /// </summary>
    bool IsAuthenticated();

    /// <summary>
    /// Pobiera aktualny access token jeśli jest ważny.
    /// Automatycznie odświeża token jeśli wygasł.
    /// </summary>
    Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default);
}
