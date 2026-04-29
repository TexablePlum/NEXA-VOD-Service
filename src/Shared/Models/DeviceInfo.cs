namespace Nexa.Shared.Models;

/// <summary>
/// Informacje o zarejestrowanym urządzeniu użytkownika.
/// </summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsCryptographicallyVerified { get; set; }
}
