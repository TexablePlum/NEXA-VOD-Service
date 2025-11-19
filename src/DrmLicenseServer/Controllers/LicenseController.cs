using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexa.DrmLicenseServer.Services;
using Nexa.Shared.Models;
using Nexa.Shared.Exceptions;
using System.Security.Claims;

namespace Nexa.DrmLicenseServer.Controllers;

/// <summary>
/// Controller do pobierania licencji DRM (CEK).
/// Wymaga autentykacji JWT.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LicenseController : ControllerBase
{
    private readonly LicenseService _licenseService;
    private readonly UserService _userService;
    private readonly ILogger<LicenseController> _logger;

    public LicenseController(
        LicenseService licenseService,
        UserService userService,
        ILogger<LicenseController> logger)
    {
        _licenseService = licenseService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Pobiera licencje (CEK) dla wszystkich dostępnych jakości contentu.
    /// Zwraca klucze zaszyfrowane public keyem urządzenia.
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// Wymaga deviceId w query parameter (urządzenie musi być wcześniej zarejestrowane).
    /// </summary>
    [HttpGet("{contentId}")]
    [ProducesResponseType(typeof(MultiQualityLicenseResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult<MultiQualityLicenseResponse>> GetLicenses(
        string contentId,
        [FromQuery] string deviceId,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        if (string.IsNullOrEmpty(deviceId))
        {
            throw new ValidationException("Device ID is required. Please register your device first at POST /api/device/register");
        }

        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        var licenses = await _licenseService.GetAllLicensesAsync(contentId, user, deviceId, ct);

        return Ok(licenses);
    }

    /// <summary>
    /// Aktualizuje heartbeat dla licencji contentu.
    /// Klient powinien wysyłać heartbeat co 30-60 sekund podczas aktywnego odtwarzania.
    /// Stream jest uznawany za aktywny tylko jeśli ostatni heartbeat był &lt; 2 minuty temu.
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// </summary>
    [HttpPost("{contentId}/heartbeat")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult> Heartbeat(
        string contentId,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        await _licenseService.HeartbeatAsync(contentId, user, ct);

        return Ok();
    }

    /// <summary>
    /// Usuwa (revoke) licencje dla contentu - zwalnia slot concurrent stream.
    /// Używane gdy user zatrzymuje odtwarzanie przed wygaśnięciem licencji.
    /// User może usunąć tylko swoje licencje (sprawdzane po userId z JWT).
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// </summary>
    [HttpDelete("{contentId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult> RevokeLicense(
        string contentId,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        // User może usunąć tylko swoje licencje
        await _licenseService.RevokeLicenseAsync(contentId, user, ct);

        return Ok();
    }
}
