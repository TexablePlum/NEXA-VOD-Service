using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

/// <summary>
/// Reprezentuje konto użytkownika w systemie.
/// Przechowuje dane logowania, plan subskrypcji
/// oraz powiązane encje takie jak tokeny, licencje i urządzenia.
/// </summary>
[Table("users")]
public class UserEntity
{
    /// <summary>
    /// Identyfikator użytkownika (UUID).
    /// </summary>
    [Key]
    [Column("user_id")]
    [MaxLength(36)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Adres e-mail użytkownika (unikalny w systemie).
    /// </summary>
    [Column("email")]
    [MaxLength(255)]
    [Required]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hash hasła zgodny z mechanizmem używanym przez backend.
    /// </summary>
    [Column("password_hash")]
    [MaxLength(255)]
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Nazwa aktywnego planu subskrypcji.
    /// </summary>
    [Column("plan")]
    [MaxLength(20)]
    [Required]
    public string Plan { get; set; } = "free";

    /// <summary>
    /// Data utworzenia konta.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Czy konto jest aktywne.
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Lista refresh tokenów wygenerowanych dla użytkownika.
    /// </summary>
    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = new List<RefreshTokenEntity>();

    /// <summary>
    /// Licencje przypisane użytkownikowi dla poszczególnych treści.
    /// </summary>
    public ICollection<IssuedLicenseEntity> IssuedLicenses { get; set; } = new List<IssuedLicenseEntity>();

    /// <summary>
    /// Klucze publiczne urządzeń powiązanych z użytkownikiem.
    /// </summary>
    public ICollection<UserDeviceKeyEntity> DeviceKeys { get; set; } = new List<UserDeviceKeyEntity>();
}
