using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Request model for secure CEK import via admin API
/// SECURITY: This endpoint is protected by admin role + IP whitelist
/// </summary>
public class CekImportRequest
{
    /// <summary>
    /// Content ID (GUID format, alphanumeric)
    /// </summary>
    [Required(ErrorMessage = "ContentId jest wymagany")]
    [RegularExpression(@"^[a-zA-Z0-9\-]+$", ErrorMessage = "ContentId może zawierać tylko litery, cyfry i myślniki")]
    [MaxLength(64, ErrorMessage = "ContentId nie może być dłuższy niż 64 znaki")]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Quality level (e.g., "480p", "720p", "1080p", "4k")
    /// </summary>
    [Required(ErrorMessage = "Quality jest wymagany")]
    [RegularExpression(@"^(480p|720p|1080p|4k)$", ErrorMessage = "Quality musi być: 480p, 720p, 1080p lub 4k")]
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Content Encryption Key (CEK) in hex format (32 hex characters = 128-bit key)
    /// SECURITY: Transmitted only in memory, never logged or persisted in plaintext
    /// </summary>
    [Required(ErrorMessage = "Cek jest wymagany")]
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "CEK musi być 32 znaki hex (128-bit key)")]
    public string Cek { get; set; } = string.Empty;

    /// <summary>
    /// Key ID (GUID without dashes, 32 hex characters)
    /// Used in DASH manifest for key identification
    /// </summary>
    [Required(ErrorMessage = "KeyId jest wymagany")]
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "KeyId musi być 32 znaki hex (GUID bez myślników)")]
    public string KeyId { get; set; } = string.Empty;
}
