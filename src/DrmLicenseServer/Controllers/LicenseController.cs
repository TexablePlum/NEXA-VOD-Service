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
    /// Pobiera licencję (CEK) dla contentu w określonej jakości.
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// </summary>
    [HttpGet("{contentId}/{quality}")]
    [ProducesResponseType(typeof(LicenseResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult<LicenseResponse>> GetLicense(
        string contentId,
        string quality,
        CancellationToken ct)
    {
        // Pobiera userId z JWT claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        // Pobiera dane użytkownika
        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        // Pobiera licencję
        var license = await _licenseService.GetLicenseAsync(contentId, quality, user, ct);

        return Ok(license);
    }

    /// <summary>
    /// Pobiera listę dostępnych jakości dla contentu.
    /// Zwraca tylko jakości które faktycznie istnieją dla contentu
    /// i są dozwolone dla planu użytkownika.
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// </summary>
    [HttpGet("{contentId}/qualities")]
    [ProducesResponseType(typeof(AvailableQualitiesResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult<AvailableQualitiesResponse>> GetAvailableQualities(
        string contentId,
        CancellationToken ct)
    {
        // Pobiera userId z JWT claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        // Pobiera dane użytkownika
        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        // Pobiera dostępne jakości
        var qualities = await _licenseService.GetAvailableQualitiesForUserAsync(contentId, user, ct);

        var response = new AvailableQualitiesResponse
        {
            ContentId = contentId,
            UserPlan = user.Plan,
            MaxQuality = Nexa.Shared.Constants.Plans.GetMaxQuality(user.Plan),
            Qualities = qualities
        };

        return Ok(response);
    }

    /// <summary>
    /// Odnawia licencję (CEK) dla contentu w określonej jakości.
    /// Pozwala odświeżyć licencję przed jej wygaśnięciem.
    /// Wymaga tokenu JWT w header Authorization: Bearer {token}.
    /// </summary>
    [HttpPost("{contentId}/{quality}/renew")]
    [ProducesResponseType(typeof(LicenseResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<ActionResult<LicenseResponse>> RenewLicense(
        string contentId,
        string quality,
        CancellationToken ct)
    {
        // Pobiera userId z JWT claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT token missing userId claim");
            throw new UnauthorizedException("Token JWT nie zawiera identyfikatora użytkownika.");
        }

        // Pobiera dane użytkownika
        var user = await _userService.GetUserByIdAsync(userId, ct);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedException("Użytkownik nie istnieje lub został dezaktywowany.");
        }

        // Odnawia licencję (sprawdza threshold i wydaje nową licencję)
        var license = await _licenseService.RenewLicenseAsync(contentId, quality, user, ct);

        return Ok(license);
    }
}
