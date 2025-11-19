namespace Nexa.Shared.Models;

/// <summary>
/// Odpowiedź zawierająca licencje DRM (CEK) dla wszystkich dostępnych jakości contentu.
/// Zwraca wszystkie klucze w jednym requeście, co upraszcza logikę po stronie klienta.
/// </summary>
public class MultiQualityLicenseResponse
{
    /// <summary>
    /// Content ID.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Plan użytkownika.
    /// </summary>
    public string UserPlan { get; set; } = string.Empty;

    /// <summary>
    /// Maksymalna jakość dostępna dla planu użytkownika.
    /// </summary>
    public string MaxQuality { get; set; } = string.Empty;

    /// <summary>
    /// Czas wygaśnięcia licencji (wspólny dla wszystkich jakości).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Lista licencji dla poszczególnych jakości.
    /// Zawiera tylko jakości które faktycznie istnieją dla tego contentu
    /// i są dozwolone dla planu użytkownika.
    /// </summary>
    public List<QualityLicense> Licenses { get; set; } = new();

    /// <summary>
    /// Liczba dostępnych licencji.
    /// </summary>
    public int Count => Licenses.Count;
}

/// <summary>
/// Licencja DRM dla pojedynczej jakości.
/// </summary>
public class QualityLicense
{
    /// <summary>
    /// Jakość (480p, 720p, 1080p, 1440p, 2160p).
    /// </summary>
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Content Encryption Key zaszyfrowany public keyem urządzenia (Base64 encoded).
    /// Klient musi odszyfrować używając swojego private key (TPM/TEE).
    /// Format: RSA-OAEP-SHA256 encrypted CEK.
    /// </summary>
    public string EncryptedKey { get; set; } = string.Empty;

    /// <summary>
    /// Key ID w formacie hex (UUID bez kresek).
    /// </summary>
    public string KeyId { get; set; } = string.Empty;
}
