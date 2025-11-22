using Nexa.Shared.Exceptions;
using System.Text.RegularExpressions;

namespace Nexa.DrmLicenseServer.Services.License;

/// <summary>
/// Serwis do walidacji danych wejściowych dla operacji licencji.
/// </summary>
public class LicenseValidationService
{
    private readonly ILogger<LicenseValidationService> _logger;

    // Whitelist-a dozwolonych znaków w ContentId
    // Tylko alfanumeryczne, myślnik i podkreślenie, długość 1-128 znaków
    private static readonly Regex ContentIdPattern = new(@"^[a-zA-Z0-9_-]{1,128}$", RegexOptions.Compiled);

    public LicenseValidationService(ILogger<LicenseValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Waliduje Content ID (format i długość).
    /// </summary>
    public void ValidateContentId(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        if (!ContentIdPattern.IsMatch(contentId))
        {
            _logger.LogWarning("Invalid ContentId format rejected: {ContentId}", contentId);
            throw new ValidationException(
                "Nieprawidłowy format Content ID. Dozwolone tylko znaki alfanumeryczne, myślnik i podkreślenie (1-128 znaków).");
        }
    }

    /// <summary>
    /// Waliduje Content ID i sprawdza path traversal.
    /// </summary>
    public void ValidateContentIdWithPathTraversal(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ValidationException("Content ID nie może być pusty.");
        }

        // Path traversal protection
        if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
        {
            _logger.LogWarning("Path traversal attempt blocked in request: {ContentId}", contentId);
            throw new ValidationException("Nieprawidłowy format Content ID.");
        }
    }

    /// <summary>
    /// Waliduje Device ID.
    /// </summary>
    public void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ValidationException("Device ID nie może być pusty.");
        }
    }
}
