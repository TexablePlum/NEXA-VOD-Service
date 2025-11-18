namespace Nexa.Shared.Models;

/// <summary>
/// Model użytkownika w systemie NEXA.
/// </summary>
public class User
{
    /// <summary>
    /// Unikalny identyfikator użytkownika (UUID).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Email użytkownika (unikalny).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hash hasła (bcrypt).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Plan subskrypcji: free, basic, pro.
    /// </summary>
    public string Plan { get; set; } = Constants.Plans.FREE;

    /// <summary>
    /// Data utworzenia konta.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Czy konto jest aktywne.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
