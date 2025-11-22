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
    /// Quality level (144p - 8K)
    /// Supported: 144p, 240p, 360p, 480p, 720p, 1080p, 1440p, 2160p, 4320p
    /// </summary>
    [Required(ErrorMessage = "Quality jest wymagany")]
    [RegularExpression(@"^(144p|240p|360p|480p|720p|1080p|1440p|2160p|4320p)$",
        ErrorMessage = "Quality musi być jedną z: 144p, 240p, 360p, 480p, 720p, 1080p, 1440p, 2160p, 4320p")]
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
