using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexa.DrmLicenseServer.Data.Entities;

/// <summary>
/// Przechowuje public keys urządzeń użytkownika dla szyfrowania CEK.
/// Każde urządzenie musi być zarejestrowane z public keyem (RSA).
/// Public key powinien być zabezpieczony w TPM/TEE na urządzeniu klienta.
/// </summary>
[Table("user_device_keys")]
public class UserDeviceKeyEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Unikalny identyfikator urządzenia (UUID generowany przez klienta).
    /// </summary>
    [Column("device_id")]
    [MaxLength(64)]
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Nazwa urządzenia (opcjonalna, dla identyfikacji przez użytkownika).
    /// </summary>
    [Column("device_name")]
    [MaxLength(128)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Public key RSA (PEM format - X.509 SubjectPublicKeyInfo).
    /// Długość: ~450-800 znaków dla RSA 2048-4096 bit.
    /// </summary>
    [Column("public_key_pem")]
    [MaxLength(2048)]
    [Required]
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// TPM Attestation Quote (opcjonalne - do przyszłej weryfikacji TPM).
    /// Format: Base64 encoded TPM quote.
    /// </summary>
    [Column("tpm_attestation")]
    [MaxLength(4096)]
    public string? TpmAttestation { get; set; }

    /// <summary>
    /// Data rejestracji urządzenia.
    /// </summary>
    [Column("registered_at")]
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ostatnie użycie (aktualizowane przy pobieraniu licencji).
    /// </summary>
    [Column("last_used_at")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Czy urządzenie jest aktywne.
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
