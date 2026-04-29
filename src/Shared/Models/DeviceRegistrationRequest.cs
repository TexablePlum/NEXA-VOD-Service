using System.ComponentModel.DataAnnotations;

namespace Nexa.Shared.Models;

/// <summary>
/// Request do rejestracji urządzenia z public keyem.
/// </summary>
public class DeviceRegistrationRequest
{
    /// <summary>
    /// Unikalny identyfikator urządzenia (UUID generowany przez klienta).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Opcjonalna nazwa urządzenia (np. "iPhone 13 Pro", "Windows PC").
    /// </summary>
    [MaxLength(128)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Public key RSA w formacie PEM (X.509 SubjectPublicKeyInfo).
    /// Klucz powinien być wygenerowany i zabezpieczony w TPM/TEE urządzenia.
    /// Minimum: RSA 2048 bits.
    /// </summary>
    [Required]
    [MaxLength(3000)]
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Losowe wyzwanie (Nonce) otrzymane z serwera, zabezpieczające przed Replay Attack.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Podpis kryptograficzny (RSA-SHA256) wygenerowany kluczem prywatnym urządzenia.
    /// Dane podpisywane: "DeviceId|Nonce".
    /// Potwierdza rzeczywiste posiadanie zgłaszanego klucza.
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string SignatureBase64 { get; set; } = string.Empty;
}
