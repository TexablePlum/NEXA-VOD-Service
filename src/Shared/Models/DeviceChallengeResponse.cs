namespace Nexa.Shared.Models;

/// <summary>
/// Odpowiedź serwera z wyzwaniem (Nonce) potrzebnym do wykazania własności klucza z użyciem TPM/Cng.
/// </summary>
public class DeviceChallengeResponse
{
    /// <summary>
    /// Losowy, kryptograficznie bezpieczny ciąg znaków (Nonce).
    /// </summary>
    public string Nonce { get; set; } = string.Empty;
}
