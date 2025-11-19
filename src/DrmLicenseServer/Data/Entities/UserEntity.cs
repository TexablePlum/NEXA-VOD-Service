using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

[Table("users")]
public class UserEntity
{
    [Key]
    [Column("user_id")]
    [MaxLength(36)]
    public string UserId { get; set; } = string.Empty;

    [Column("email")]
    [MaxLength(255)]
    [Required]
    public string Email { get; set; } = string.Empty;

    [Column("password_hash")]
    [MaxLength(255)]
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("plan")]
    [MaxLength(20)]
    [Required]
    public string Plan { get; set; } = "free";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = new List<RefreshTokenEntity>();
    public ICollection<IssuedLicenseEntity> IssuedLicenses { get; set; } = new List<IssuedLicenseEntity>();
    public ICollection<UserDeviceKeyEntity> DeviceKeys { get; set; } = new List<UserDeviceKeyEntity>();
}
