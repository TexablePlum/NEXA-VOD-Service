using System.Security.Cryptography;
using System.Text;

namespace Nexa.DrmLicenseServer.Services;

/// <summary>
/// Serwis do szyfrowania/deszyfrowania CEK (Content Encryption Keys).
/// Używa AES-256-GCM z master keyem do envelope encryption.
/// - CEK są szyfrowane przed zapisem do Redis (at-rest encryption)
/// - Dodatkowe szyfrowanie kluczem publicznym użytkownika przed wysyłką
/// </summary>
public class CekEncryptionService
{
    private readonly byte[] _masterKey;
    private readonly ILogger<CekEncryptionService> _logger;

    public CekEncryptionService(IConfiguration configuration, ILogger<CekEncryptionService> logger)
    {
        var masterKeyBase64 = configuration["Security:CekMasterKey"]
            ?? throw new InvalidOperationException(
                "CEK Master Key not configured. Set Security:CekMasterKey in appsettings or environment variable.");

        try
        {
            _masterKey = Convert.FromBase64String(masterKeyBase64);

            if (_masterKey.Length != 32)
            {
                throw new InvalidOperationException(
                    $"CEK Master Key must be 32 bytes (256 bits). Current length: {_masterKey.Length} bytes. " +
                    "Generate with: openssl rand -base64 32");
            }

            _logger = logger;
            _logger.LogInformation("CekEncryptionService initialized with {KeySize}-bit master key", _masterKey.Length * 8);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "CEK Master Key must be valid Base64 string. Generate with: openssl rand -base64 32");
        }
    }

    /// <summary>
    /// Szyfruje CEK za pomocą master keya (AES-256-GCM).
    /// Zwraca: Base64(nonce + tag + ciphertext)
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            throw new ArgumentException("Plaintext cannot be empty", nameof(plaintext));
        }

        try
        {
            using var aes = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);

            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

            RandomNumberGenerator.Fill(nonce);

            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Format: nonce(12) + tag(16) + ciphertext
            var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt CEK");
            throw new InvalidOperationException("Encryption failed", ex);
        }
    }

    /// <summary>
    /// Deszyfruje CEK za pomocą master keya (AES-256-GCM).
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new ArgumentException("Ciphertext cannot be empty", nameof(ciphertext));
        }

        try
        {
            var encryptedBytes = Convert.FromBase64String(ciphertext);

            var nonceSize = AesGcm.NonceByteSizes.MaxSize; // 12 bytes
            var tagSize = AesGcm.TagByteSizes.MaxSize; // 16 bytes

            if (encryptedBytes.Length < nonceSize + tagSize)
            {
                throw new InvalidOperationException(
                    $"Invalid ciphertext format. Expected at least {nonceSize + tagSize} bytes, got {encryptedBytes.Length}");
            }

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var ciphertextBytes = new byte[encryptedBytes.Length - nonceSize - tagSize];

            Buffer.BlockCopy(encryptedBytes, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(encryptedBytes, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(encryptedBytes, nonceSize + tagSize, ciphertextBytes, 0, ciphertextBytes.Length);

            using var aes = new AesGcm(_masterKey, tagSize);
            var plaintextBytes = new byte[ciphertextBytes.Length];

            aes.Decrypt(nonce, ciphertextBytes, tag, plaintextBytes);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Invalid ciphertext format. Must be valid Base64 string.");
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Decryption failed - possible tampering or wrong master key");
            throw new InvalidOperationException("Decryption failed. Data may be corrupted or tampered.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt CEK");
            throw new InvalidOperationException("Decryption failed", ex);
        }
    }

    /// <summary>
    /// Generuje nowy master key (do użycia przez admina).
    /// </summary>
    public static string GenerateMasterKey()
    {
        var keyBytes = new byte[32]; // 256 bits
        RandomNumberGenerator.Fill(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }
}
