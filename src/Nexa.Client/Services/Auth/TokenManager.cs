using System;
using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Zarządza tokenami autoryzacji z bezpiecznym przechowywaniem.
/// AccessToken w pamięci RAM, RefreshToken może być w RAM lub Windows Credential Manager (Remember Me).
/// </summary>
public class TokenManager : ITokenManager
{
    private const string CredentialResourceName = "NEXA_RefreshToken";

    private string? _accessToken;
    private DateTime? _accessTokenExpiry;
    private byte[]? _encryptedRefreshToken; // RefreshToken w RAM (jeśli Remember Me = false)
    private string? _currentEmail; // Email użytkownika (do Credential Manager)
    private bool _isPersisted; // Czy ostatnio używano persistRefreshToken = true

    /// <inheritdoc/>
    public event EventHandler? AccessTokenExpired;

    /// <inheritdoc/>
    public void StoreTokens(string accessToken, string refreshToken, int expiresInSeconds, string email, bool persistRefreshToken = false)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token nie może być pusty", nameof(accessToken));

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token nie może być pusty", nameof(refreshToken));

        if (expiresInSeconds <= 0)
            throw new ArgumentException("Czas wygaśnięcia musi być dodatni", nameof(expiresInSeconds));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email nie może być pusty", nameof(email));

        // Przechowuj Access Token w pamięci
        _accessToken = accessToken;
        _accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds);
        _currentEmail = email;
        _isPersisted = persistRefreshToken;

        if (persistRefreshToken)
        {
            // Remember Me = true → zapisz w Windows Credential Manager
            SaveRefreshTokenToVault(email, refreshToken);
            _encryptedRefreshToken = null; // Wyczyść z RAM
        }
        else
        {
            // Remember Me = false → zapisz tylko w RAM (DPAPI)
            var refreshTokenBytes = Encoding.UTF8.GetBytes(refreshToken);
            _encryptedRefreshToken = ProtectedData.Protect(
                refreshTokenBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            // Usuń z Credential Manager jeśli istnieje
            RemoveSavedRefreshToken(email);
        }
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
        // Priorytet 1: RefreshToken w RAM (jeśli Remember Me = false)
        if (_encryptedRefreshToken != null)
        {
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
            }
        }

        // Priorytet 2: RefreshToken w Credential Manager (jeśli Remember Me = true)
        if (!string.IsNullOrEmpty(_currentEmail))
        {
            return LoadRefreshTokenFromVault(_currentEmail);
        }

        // Fallback: Po restarcie _currentEmail jest null, więc szukaj w Vault
        if (HasSavedRefreshToken(out var savedEmail) && !string.IsNullOrEmpty(savedEmail))
        {
            _currentEmail = savedEmail; // Zapamiętaj email dla przyszłych wywołań
            _isPersisted = true; // Token był w Vault, więc był persisted
            return LoadRefreshTokenFromVault(savedEmail);
        }

        return null;
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
        // Zalogowany jeśli ma AccessToken i (RefreshToken w RAM lub w Vault)
        return _accessToken != null && GetRefreshToken() != null;
    }

    /// <inheritdoc/>
    public void ClearTokens()
    {
        _accessToken = null;
        _accessTokenExpiry = null;
        _encryptedRefreshToken = null;

        // Usuń również z Windows Credential Manager
        if (!string.IsNullOrEmpty(_currentEmail))
        {
            RemoveSavedRefreshToken(_currentEmail);
        }

        _currentEmail = null;
        _isPersisted = false;
    }

    /// <inheritdoc/>
    public DateTime? GetAccessTokenExpiry()
    {
        return _accessTokenExpiry;
    }

    /// <inheritdoc/>
    public bool HasSavedRefreshToken(out string? email)
    {
        email = null;

        try
        {
            var vault = new PasswordVault();
            var credentials = vault.FindAllByResource(CredentialResourceName);

            if (credentials != null && credentials.Count > 0)
            {
                // Pobierz pierwszy znaleziony credential (powinien być tylko jeden)
                var credential = credentials[0];
                email = credential.UserName;
                return true;
            }
        }
        catch (Exception)
        {
            // Brak credentials lub błąd dostępu - ignoruj
        }

        return false;
    }

    /// <inheritdoc/>
    public void RemoveSavedRefreshToken(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(CredentialResourceName, email);
            vault.Remove(credential);
        }
        catch (Exception)
        {
            // Credential nie istnieje lub błąd - ignoruj
        }
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

    /// <inheritdoc/>
    public string? GetCurrentEmail()
    {
        return _currentEmail;
    }

    /// <inheritdoc/>
    public bool IsPersisted()
    {
        return _isPersisted;
    }

    // ==================== Private Helpers - Credential Manager ====================

    private void SaveRefreshTokenToVault(string email, string refreshToken)
    {
        try
        {
            var vault = new PasswordVault();

            // Usuń stary credential jeśli istnieje
            try
            {
                var oldCredential = vault.Retrieve(CredentialResourceName, email);
                vault.Remove(oldCredential);
            }
            catch
            {
                // Stary credential nie istnieje - OK
            }

            // Dodaj nowy credential
            var credential = new PasswordCredential(CredentialResourceName, email, refreshToken);
            vault.Add(credential);
        }
        catch (Exception ex)
        {
            // Błąd zapisu do Credential Manager - fallback do RAM
            System.Diagnostics.Debug.WriteLine($"[TokenManager] Błąd zapisu do Credential Manager: {ex.Message}");

            // Zapisz w RAM jako backup
            var refreshTokenBytes = Encoding.UTF8.GetBytes(refreshToken);
            _encryptedRefreshToken = ProtectedData.Protect(
                refreshTokenBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
        }
    }

    private string? LoadRefreshTokenFromVault(string email)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(CredentialResourceName, email);

            // Pobierz hasło (refresh token)
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception)
        {
            // Credential nie istnieje lub błąd - zwróć null
            return null;
        }
    }
}
