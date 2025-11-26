using System;
using System.Security.Cryptography;
using System.Text;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Zarządza tokenami autoryzacji z bezpiecznym przechowywaniem.
/// AccessToken w pamięci RAM, RefreshToken szyfrowany przez Windows DPAPI.
/// </summary>
public class TokenManager : ITokenManager
{
    private string? _accessToken;
    private DateTime? _accessTokenExpiry;
    private byte[]? _encryptedRefreshToken;

    /// <inheritdoc/>
    public event EventHandler? AccessTokenExpired;

    /// <inheritdoc/>
    public void StoreTokens(string accessToken, string refreshToken, int expiresInSeconds)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token nie może być pusty", nameof(accessToken));

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token nie może być pusty", nameof(refreshToken));

        if (expiresInSeconds <= 0)
            throw new ArgumentException("Czas wygaśnięcia musi być dodatni", nameof(expiresInSeconds));

        // Przechowaj Access Token w pamięci
        _accessToken = accessToken;
        _accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        // Zaszyfruj i przechowaj Refresh Token
        var refreshTokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        _encryptedRefreshToken = ProtectedData.Protect(
            refreshTokenBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser
        );
    }

    /// <inheritdoc/>
    public string? GetAccessToken()
    {
        if (!IsAccessTokenValid())
        {
            return null;
        }

        return _accessToken;
    }

    /// <inheritdoc/>
    public string? GetRefreshToken()
    {
        if (_encryptedRefreshToken == null)
            return null;

        try
        {
            var decryptedBytes = ProtectedData.Unprotect(
                _encryptedRefreshToken,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException)
        {
            // Token został uszkodzony lub manipulowany
            _encryptedRefreshToken = null;
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsAccessTokenValid(int bufferSeconds = 60)
    {
        if (_accessToken == null || _accessTokenExpiry == null)
            return false;

        // Sprawdź czy token nie wygasł (z buforem bezpieczeństwa)
        var expiryWithBuffer = _accessTokenExpiry.Value.AddSeconds(-bufferSeconds);
        return DateTime.UtcNow < expiryWithBuffer;
    }

    /// <inheritdoc/>
    public bool IsAuthenticated()
    {
        return _accessToken != null && _encryptedRefreshToken != null;
    }

    /// <inheritdoc/>
    public void ClearTokens()
    {
        _accessToken = null;
        _accessTokenExpiry = null;
        _encryptedRefreshToken = null;
    }

    /// <inheritdoc/>
    public DateTime? GetAccessTokenExpiry()
    {
        return _accessTokenExpiry;
    }

    /// <summary>
    /// Sprawdza wygaśnięcie tokenu i wywołuje event jeśli wygasł.
    /// Metoda może być wywoływana cyklicznie przez timer.
    /// </summary>
    public void CheckTokenExpiration()
    {
        if (_accessToken != null && !IsAccessTokenValid(bufferSeconds: 120))
        {
            AccessTokenExpired?.Invoke(this, EventArgs.Empty);
        }
    }
}
