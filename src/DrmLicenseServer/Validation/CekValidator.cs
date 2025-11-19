using System.Text.RegularExpressions;

namespace Nexa.DrmLicenseServer.Validation;

/// <summary>
/// Walidator dla Content Encryption Keys (CEK).
///
/// CEK Format:
/// - Długość: 32 znaki hex (16 bytes)
/// - Format: [0-9a-fA-F]{32}
/// - Przykład: "a1b2c3d4e5f67890abcdef1234567890"
/// </summary>
public static partial class CekValidator
{
    private const int CEK_HEX_LENGTH = 32; // 16 bytes = 32 hex chars
    private const int CEK_MAX_LENGTH = 64; // Maksymalna dopuszczalna długość (32 bytes)

    [GeneratedRegex("^[0-9a-fA-F]+$", RegexOptions.Compiled)]
    private static partial Regex HexRegex();

    /// <summary>
    /// Waliduje CEK (Content Encryption Key).
    /// </summary>
    /// <param name="cek">CEK do walidacji (hex string)</param>
    /// <param name="errorMessage">Wiadomość błędu jeśli walidacja się nie powiedzie</param>
    /// <returns>True jeśli CEK jest poprawny</returns>
    public static bool Validate(string cek, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(cek))
        {
            errorMessage = "CEK cannot be empty";
            return false;
        }

        // DoS protection - maksymalna długość
        if (cek.Length > CEK_MAX_LENGTH)
        {
            errorMessage = $"CEK is too long. Maximum length: {CEK_MAX_LENGTH} characters, got: {cek.Length}";
            return false;
        }

        // Sprawdza czy jest hex string
        if (!HexRegex().IsMatch(cek))
        {
            errorMessage = "CEK must be a valid hexadecimal string (only 0-9, a-f, A-F characters allowed)";
            return false;
        }

        // Sprawdza czy ma poprawną długość (16 bytes = 32 hex chars)
        if (cek.Length != CEK_HEX_LENGTH)
        {
            errorMessage = $"CEK must be {CEK_HEX_LENGTH} characters long (16 bytes in hex), got: {cek.Length} characters";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Waliduje KeyId.
    /// </summary>
    public static bool ValidateKeyId(string keyId, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(keyId))
        {
            errorMessage = "KeyId cannot be empty";
            return false;
        }

        // DoS protection - maksymalna długość
        if (keyId.Length > 128)
        {
            errorMessage = $"KeyId is too long. Maximum length: 128 characters, got: {keyId.Length}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Generuje przykładowy CEK (16 bytes = 32 hex chars) do testów.
    /// Używa kryptograficznie bezpiecznego RandomNumberGenerator.
    /// </summary>
    public static string GenerateSampleCek()
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
