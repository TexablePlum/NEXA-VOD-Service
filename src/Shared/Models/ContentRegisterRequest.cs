using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Request model for content registration in DRM server.
/// Rejestruje content i importuje wszystkie CEK-i w jednej transakcji.
/// SECURITY: This endpoint is protected by admin role + IP whitelist
/// </summary>
public class ContentRegisterRequest
{
    /// <summary>
    /// Content ID (alphanumeric with dashes)
    /// </summary>
    [Required(ErrorMessage = "ContentId jest wymagany")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "ContentId może zawierać tylko litery, cyfry, myślniki i podkreślenia")]
    [MaxLength(128, ErrorMessage = "ContentId nie może być dłuższy niż 128 znaków")]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Lista kluczy szyfrujących (CEK) dla wszystkich jakości contentu.
    /// Każda jakość ma swój własny CEK.
    /// </summary>
    [Required(ErrorMessage = "Lista CEK-ów jest wymagana")]
    [MinLength(1, ErrorMessage = "Musisz podać przynajmniej jeden CEK")]
    public List<CekData> Ceks { get; set; } = new();
}

/// <summary>
/// Dane CEK dla pojedynczej jakości contentu.
/// </summary>
public class CekData
{
    /// <summary>
    /// Jakość wideo (np. "480p", "720p", "1080p", "4k")
    /// </summary>
    [Required(ErrorMessage = "Quality jest wymagana")]
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Content Encryption Key (CEK) w formacie hex (32 znaki)
    /// </summary>
    [Required(ErrorMessage = "CEK jest wymagany")]
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "CEK musi być 32-znakowym ciągiem hex")]
    public string Cek { get; set; } = string.Empty;

    /// <summary>
    /// Key ID (UUID bez myślników)
    /// </summary>
    [Required(ErrorMessage = "KeyId jest wymagany")]
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "KeyId musi być 32-znakowym UUID bez myślników")]
    public string KeyId { get; set; } = string.Empty;
}
