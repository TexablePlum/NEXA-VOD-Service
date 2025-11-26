using Nexa.Client.Services.Base;
using Nexa.Shared.Models;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Serwis autoryzacji komunikujący się z DRM backend.
/// Zarządza logowaniem, rejestracją i odświeżaniem tokenów.
/// </summary>
public class AuthService : BaseApiService, IAuthService
{
    private readonly ITokenManager _tokenManager;

    public AuthService(HttpClient httpClient, ITokenManager tokenManager)
        : base(httpClient)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email nie może być pusty", nameof(email));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Hasło nie może być puste", nameof(password));

        var request = new LoginRequest
        {
            Email = email.Trim(),
            Password = password
        };

        var response = await PostAsync<LoginRequest, AuthResponse>(
            "api/auth/login",
            request,
            ct
        );

        // Przechowaj tokeny i ustaw Authorization header
        StoreTokensAndSetHeader(response);

        return response;
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> RegisterAsync(string email, string password, string plan = "free", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email nie może być pusty", nameof(email));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Hasło nie może być puste", nameof(password));

        var request = new RegisterRequest
        {
            Email = email.Trim(),
            Password = password,
            Plan = plan
        };

        var response = await PostAsync<RegisterRequest, AuthResponse>(
            "api/auth/register",
            request,
            ct
        );

        // Przechowaj tokeny i ustaw Authorization header
        StoreTokensAndSetHeader(response);

        return response;
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> RefreshTokenAsync(CancellationToken ct = default)
    {
        var refreshToken = _tokenManager.GetRefreshToken();

        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException("Brak refresh tokenu. Użytkownik musi się zalogować ponownie.");
        }

        var request = new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        };

        var response = await PostAsync<RefreshTokenRequest, AuthResponse>(
            "api/auth/refresh",
            request,
            ct
        );

        // Przechowaj nowe tokeny i zaktualizuj Authorization header
        StoreTokensAndSetHeader(response);

        return response;
    }

    /// <inheritdoc/>
    public void Logout()
    {
        _tokenManager.ClearTokens();

        // Usuń Authorization header
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    /// <inheritdoc/>
    public bool IsAuthenticated()
    {
        return _tokenManager.IsAuthenticated();
    }

    /// <inheritdoc/>
    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        // Sprawdź czy token jest ważny
        if (_tokenManager.IsAccessTokenValid())
        {
            return _tokenManager.GetAccessToken();
        }

        // Token wygasł - spróbuj odświeżyć
        if (_tokenManager.GetRefreshToken() != null)
        {
            try
            {
                var response = await RefreshTokenAsync(ct);
                return response.AccessToken;
            }
            catch
            {
                // Refresh się nie powiódł - wymagane ponowne logowanie
                Logout();
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Pomocnicza metoda do przechowywania tokenów i ustawiania Authorization header.
    /// </summary>
    private void StoreTokensAndSetHeader(AuthResponse response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));

        // Przechowaj tokeny w TokenManager
        _tokenManager.StoreTokens(
            response.AccessToken,
            response.RefreshToken,
            response.ExpiresIn
        );

        // Ustaw Bearer token dla przyszłych requestów
        SetAccessToken(response.AccessToken);
    }
}
