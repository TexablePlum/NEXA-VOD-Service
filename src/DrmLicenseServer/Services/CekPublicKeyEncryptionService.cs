using System.Security.Cryptography;
using System.Text;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis do szyfrowania CEK (Content Encryption Keys) public key-em RSA urządzenia użytkownika.
/// </summary>
public class CekPublicKeyEncryptionService
{
    private readonly ILogger<CekPublicKeyEncryptionService> _logger;

    public CekPublicKeyEncryptionService(ILogger<CekPublicKeyEncryptionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Szyfruje CEK (plaintext hex string) za pomocą public keya RSA.
    /// Używa RSA-OAEP z SHA-256.
    /// </summary>
    /// <param name="plaintextCek">CEK w formacie hex (32 znaki)</param>
    /// <param name="publicKeyPem">Public key RSA w formacie PEM (X.509 SubjectPublicKeyInfo)</param>
    /// <returns>Base64 encoded zaszyfrowany CEK</returns>
    public string EncryptCekWithPublicKey(string plaintextCek, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(plaintextCek))
        {
            throw new ArgumentException("CEK cannot be empty", nameof(plaintextCek));
        }

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new ArgumentException("Public key PEM cannot be empty", nameof(publicKeyPem));
        }

        try
        {
            // Importuje public key z PEM
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            // Konwertuje hex CEK (string "a1b2c3d4...") na rzeczywiste bytes
            byte[] cekBytes;
            try
            {
                cekBytes = Convert.FromHexString(plaintextCek);
            }
            catch (FormatException)
            {
                throw new ArgumentException(
                    $"CEK must be a valid hex string. Expected format: 32 hex characters (e.g., 'a1b2c3d4...'). Got length: {plaintextCek.Length}",
                    nameof(plaintextCek));
            }

            // Walidacja długości: CEK powinien być 128-bit (16 bytes) lub 256-bit (32 bytes)
            if (cekBytes.Length != 16 && cekBytes.Length != 32)
            {
                throw new ArgumentException(
                    $"CEK must be 16 bytes (128-bit) or 32 bytes (256-bit). Got: {cekBytes.Length} bytes from hex string '{plaintextCek}'",
                    nameof(plaintextCek));
            }

            // Szyfruje używając RSA-OAEP z SHA-256
            var encryptedBytes = rsa.Encrypt(cekBytes, RSAEncryptionPadding.OaepSHA256);

            return Convert.ToBase64String(encryptedBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to encrypt CEK with public key - invalid key format or key too small");
            throw new InvalidOperationException("Failed to encrypt CEK with public key. Ensure key is RSA 2048+ bits.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during CEK encryption with public key");
            throw new InvalidOperationException("Failed to encrypt CEK", ex);
        }
    }

    /// <summary>
    /// Waliduje public key PEM (sprawdza format i rozmiar klucza).
    /// Minimum: RSA 2048 bits.
    /// Rekomendowane: RSA 4096 bits.
    /// </summary>
    public bool ValidatePublicKey(string publicKeyPem, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            errorMessage = "Public key PEM cannot be empty";
            return false;
        }

        // DoS protection
        if (publicKeyPem.Length > 3000)
        {
            errorMessage = $"Public key PEM is too long. Maximum: 3000 characters, got: {publicKeyPem.Length}";
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            // Sprawdza rozmiar klucza (minimum 2048 bits)
            var keySize = rsa.KeySize;
            if (keySize < 2048)
            {
                errorMessage = $"RSA key size too small. Minimum: 2048 bits, got: {keySize} bits";
                return false;
            }

            // Sprawdza że to public key (nie może szyfrować - tylko deszyfrować)
            // Próba eksportu private key powinna się nie udać
            if (rsa.ExportParameters(false).Modulus == null)
            {
                errorMessage = "Invalid RSA public key - missing modulus";
                return false;
            }

            _logger.LogDebug("Public key validated successfully: {KeySize} bits", keySize);
            return true;
        }
        catch (CryptographicException ex)
        {
            errorMessage = $"Invalid public key format: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to validate public key: {ex.Message}";
            return false;
        }
    }
}
