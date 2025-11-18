using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Żądanie odświeżenia access tokenu.
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>
    /// Refresh token otrzymany podczas logowania.
    /// </summary>
    [Required(ErrorMessage = "Refresh token jest wymagany")]
    public string RefreshToken { get; set; } = string.Empty;
}
