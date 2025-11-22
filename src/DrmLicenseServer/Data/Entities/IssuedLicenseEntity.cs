using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

/// <summary>
/// Przechowuje informację o licencjach wydanych użytkownikowi
/// dla konkretnej treści. Licencja określa jakość, czas ważności
/// oraz parametry niezbędne do odszyfrowania strumienia.
/// </summary>
[Table("issued_licenses")]
public class IssuedLicenseEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Identyfikator użytkownika, któremu wydano licencję.
    /// </summary>
    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Id treści, której dotyczy licencja.
    /// </summary>
    [Column("content_id")]
    [MaxLength(255)]
    [Required]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Poziom jakości, do której użytkownik ma prawo.
    /// </summary>
    [Column("quality")]
    [MaxLength(20)]
    [Required]
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Data wystawienia licencji.
    /// </summary>
    [Column("issued_at")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Data wygaśnięcia licencji.
    /// </summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Ostatni heartbeat od klienta potwierdzający,
    /// że odtwarzanie nadal trwa na autoryzowanym urządzeniu.
    /// </summary>
    [Column("last_heartbeat")]
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identyfikator klucza szyfrującego (KID),
    /// którego dotyczy ta licencja.
    /// </summary>
    [Column("key_id")]
    [MaxLength(64)]
    public string? KeyId { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
