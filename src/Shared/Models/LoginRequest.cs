using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Żądanie logowania użytkownika.
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Email użytkownika.
    /// </summary>
    [Required(ErrorMessage = "Email jest wymagany")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy format email")]
    [MaxLength(255, ErrorMessage = "Email nie może być dłuższy niż 255 znaków")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hasło użytkownika.
    /// </summary>
    [Required(ErrorMessage = "Hasło jest wymagane")]
    [MaxLength(128, ErrorMessage = "Hasło nie może być dłuższe niż 128 znaków")] // SECURITY FIX: Prevent BCrypt DoS
    public string Password { get; set; } = string.Empty;
}
