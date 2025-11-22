namespace Nexa.Shared.Models;

/// <summary>
/// Response model dla importu CEK.
/// </summary>
public class CekImportResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ContentId { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
}

/// <summary>
/// Response model dla weryfikacji CEK.
/// </summary>
public class CekVerifyResponse
{
    public bool Exists { get; set; }
    public string ContentId { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string? KeyId { get; set; }
    public DateTime? ImportedAt { get; set; }
    public string? ImportedBy { get; set; }
}

/// <summary>
/// Response model dla rejestracji contentu.
/// </summary>
public class ContentRegisterResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ContentId { get; set; } = string.Empty;
    public string RequiredPlan { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public DateTime RegisteredAt { get; set; }
    public List<string> ImportedQualities { get; set; } = new();
    public int TotalCeksImported { get; set; }
}
