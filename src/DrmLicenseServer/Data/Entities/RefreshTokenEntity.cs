using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

[Table("refresh_tokens")]
public class RefreshTokenEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("token")]
    [MaxLength(255)]
    [Required]
    public string Token { get; set; } = string.Empty;

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("is_revoked")]
    public bool IsRevoked { get; set; } = false;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
