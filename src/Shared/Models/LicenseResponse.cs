namespace Nexa.Shared.Models;

/// <summary>
/// Odpowiedź z licencją DRM (CEK dla contentu).
/// </summary>
public class LicenseResponse
{
    /// <summary>
    /// Content Encryption Key w formacie hex (16 bajtów = 32 znaki hex).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Key ID w formacie hex (UUID bez kresek).
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Jakość dla której jest ten klucz (480p, 720p, 1080p, 4k).
    /// </summary>
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Content ID.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Czas wygaśnięcia licencji.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
