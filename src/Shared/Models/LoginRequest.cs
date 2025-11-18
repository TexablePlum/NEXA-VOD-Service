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
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hasło użytkownika.
    /// </summary>
    [Required(ErrorMessage = "Hasło jest wymagane")]
    public string Password { get; set; } = string.Empty;
}
