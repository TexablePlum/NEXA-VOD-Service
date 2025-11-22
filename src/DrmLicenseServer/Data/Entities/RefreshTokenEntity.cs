using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

/// <summary>
/// Przechowuje refresh tokeny wydawane użytkownikom.
/// </summary>
[Table("refresh_tokens")]
public class RefreshTokenEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Wygenerowany refresh token w postaci losowego stringa.
    /// </summary>
    [Column("token")]
    [MaxLength(255)]
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Identyfikator użytkownika, do którego należy token.
    /// </summary>
    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Data wygaśnięcia tokenu, po której nie może być użyty.
    /// </summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Data utworzenia tokenu.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Informacja, czy token został unieważniony.
    /// </summary>
    [Column("is_revoked")]
    public bool IsRevoked { get; set; } = false;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
