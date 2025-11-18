using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Żądanie rejestracji nowego użytkownika.
/// </summary>
public class RegisterRequest
{
    /// <summary>
    /// Email użytkownika.
    /// </summary>
    [Required(ErrorMessage = "Email jest wymagany")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy format email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hasło (min 8 znaków).
    /// </summary>
    [Required(ErrorMessage = "Hasło jest wymagane")]
    [MinLength(8, ErrorMessage = "Hasło musi mieć minimum 8 znaków")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Plan subskrypcji (domyślnie free).
    /// </summary>
    public string Plan { get; set; } = Constants.Plans.FREE;
}
