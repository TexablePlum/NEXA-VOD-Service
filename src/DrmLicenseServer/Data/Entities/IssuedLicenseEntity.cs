using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

[Table("issued_licenses")]
public class IssuedLicenseEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("content_id")]
    [MaxLength(255)]
    [Required]
    public string ContentId { get; set; } = string.Empty;

    [Column("quality")]
    [MaxLength(20)]
    [Required]
    public string Quality { get; set; } = string.Empty;

    [Column("issued_at")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Ostatni heartbeat od klienta. Używany do sprawdzania czy stream jest faktycznie aktywny.
    /// Stream jest aktywny jeśli LastHeartbeat < 2 minuty temu.
    /// </summary>
    [Column("last_heartbeat")]
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
